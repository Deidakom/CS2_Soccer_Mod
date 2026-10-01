#!/usr/bin/env node
// SoccerMod Arena: turns our v8-based stadium source (soccer_soccermod_stadium before the XSL
// rebuild) into the new map. Everything inside the pitch walls stays byte-identical: grass,
// lines, goals, nets, the low boards, the invisible walls, spawns, ball, sounds, pitch lights.
// Removed: the old stands, roof, underground rooms, the sky path, the wall buttons and the
// hidden marker gadget. The main door stays but can no longer open (func_brush).
// Added: the arena models (tools/arena/generate-arena.mjs), a bigger sky box, fill lights.
//
//   node --max-old-space-size=6144 tools/arena/apply-arena-vmap.mjs --in <vmap> --out <vmap> --layout <arena-layout.json> [--dry-run 1]
import fs from "node:fs";
import crypto from "node:crypto";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.in || !args.layout || (!args.out && !args["dry-run"])) throw new Error("usage: --in <vmap> --out <vmap> --layout <json> [--dry-run 1]");
const layout = JSON.parse(fs.readFileSync(args.layout, "utf8"));
const BOX = { x: 1300, y: 1700, floor: -40 };          // the pitch with its boards and walls
const MODELS = [0, 1, 2, 3, 4, 5, 6, 7].map((s) => `models/soccermod_arena/arena_${s}.vmdl`);
const SKY_MATERIAL = "materials/tools/toolsskybox.vmat";
const SKY_NAME = args.sky ?? "materials/skybox/sky_de_overpass_01.vmat";
// Owner 2026-10-01: the goals stand where CS:S (ka_soccer_xsl_stadium_b1) has them. v8's whole goal
// assembly is 7 units closer to the centre line (post front |y| 1377 instead of 1384), so frames,
// nets and the goal triggers move 7 units out at both ends. Width and height are the same.
const GOAL_SHIFT = Number(args["goal-shift"] ?? 7);
const inGoal = (pts) => pts.length > 0 && pts.every((p) => Math.abs(p[0]) <= 140 && Math.abs(p[1]) >= 1370 && Math.abs(p[1]) <= 1480);
const goalShifts = [];   // { id, type, sign }
const text = fs.readFileSync(args.in, "utf8");

// ---- element tree: every "{" is an element, its type is the quoted token before it ------------------
const nodes = [], stack = [];
let lastTokStart = -1, lastTokEnd = -1;
for (let i = 0; i < text.length; i++) {
  const c = text[i];
  if (c === '"') { const start = i; for (i++; i < text.length && text[i] !== '"'; i++) if (text[i] === "\\") i++; lastTokStart = start; lastTokEnd = i; }
  else if (c === "{") { const node = { type: text.slice(lastTokStart + 1, lastTokEnd), tokStart: lastTokStart, open: i, close: -1, parent: stack.at(-1) ?? null, kids: [] }; if (node.parent) node.parent.kids.push(node); nodes.push(node); stack.push(node); }
  else if (c === "}") stack.pop().close = i;
}
const own = (node) => { let s = "", at = node.open; for (const k of node.kids) { s += text.slice(at, k.open); at = k.close + 1; } return s + text.slice(at, node.close + 1); };
const idOf = (n) => text.slice(n.open, n.open + 200).match(/"id"\s+"elementid"\s+"([0-9a-f-]+)"/)?.[1];
const byId = new Map(nodes.filter((n) => !n.parent || n.type === "CMapMesh" || n.type === "CMapEntity").map((n) => [idOf(n), n]));
const vec = (s) => (s ? s.trim().split(/\s+/).map(Number) : null);
function angleMatrix([p, y, r]) {
  const [sp, cp, sy, cy, sr, cr] = [p, p, y, y, r, r].map((a, i) => (i % 2 ? Math.cos : Math.sin)(a * Math.PI / 180));
  return [[cp * cy, sr * sp * cy - cr * sy, cr * sp * cy + sr * sy], [cp * sy, sr * sp * sy + cr * cy, cr * sp * sy - sr * cy], [-sp, sr * cp, cr * cp]];
}
function meshPoints(mesh) {
  const head = own(mesh);
  const origin = vec(head.match(/"origin"\s+"vector3"\s+"([^"]+)"/)?.[1]) ?? [0, 0, 0], angles = vec(head.match(/"angles"\s+"qangle"\s+"([^"]+)"/)?.[1]) ?? [0, 0, 0], scales = vec(head.match(/"scales"\s+"vector3"\s+"([^"]+)"/)?.[1]) ?? [1, 1, 1];
  const R = angleMatrix(angles), block = text.slice(mesh.open, mesh.close + 1);
  const stream = block.match(/"standardAttributeName"\s+"string"\s+"position"[\s\S]*?"data"\s+"vector3_array"\s*\[([\s\S]*?)\]/);
  return [...(stream?.[1] ?? "").matchAll(/"([^"]+)"/g)].map((m) => { const l = vec(m[1]).map((x, k) => x * scales[k]); return [0, 1, 2].map((i) => origin[i] + R[i][0] * l[0] + R[i][1] * l[1] + R[i][2] * l[2]); });
}
const material = (n) => text.slice(n.open, n.close + 1).match(/"materials"\s+"string_array"\s*\[\s*"([^"]+)"/)?.[1] ?? "";
// children: inline elements and "element" "<id>" references
function childMeshes(n) {
  const out = n.kids.filter((k) => k.type === "CMapMesh");
  const list = own(n).match(/"children"\s+"element_array"\s*\[([\s\S]*?)\]/)?.[1] ?? "";
  for (const m of list.matchAll(/"element"\s+"([0-9a-f-]+)"/g)) { const c = byId.get(m[1]); if (c?.type === "CMapMesh") out.push(c); }
  return out;
}
const inBox = (p) => Math.abs(p[0]) <= BOX.x && Math.abs(p[1]) <= BOX.y;
const onPitch = (pts) => pts.length > 0 && pts.every(inBox) && Math.min(...pts.map((p) => p[2])) >= BOX.floor;

// ---- what goes ----------------------------------------------------------------------------------------
const remove = new Set(), report = { worldMeshes: { kept: {}, removed: 0 }, entities: { removed: {}, kept: {} } };
const entityMeshes = new Set();
const entities = nodes.filter((n) => n.type === "CMapEntity");
const edits = [];   // { id, apply(text) }
const propsOf = (e) => { const pk = e.kids.find((k) => k.type === "EditGameClassProps"); return pk ? text.slice(pk.open, pk.close + 1) : ""; };
const classOf = (e) => propsOf(e).match(/"classname"\s+"string"\s+"([^"]+)"/)?.[1] ?? "?";
for (const e of entities) {
  const head = own(e), props = propsOf(e), cls = props.match(/"classname"\s+"string"\s+"([^"]+)"/)?.[1] ?? "?", name = props.match(/"targetname"\s+"string"\s+"([^"]*)"/)?.[1] ?? "";
  const meshes = childMeshes(e); meshes.forEach((m) => entityMeshes.add(m));
  const pts = meshes.flatMap(meshPoints), origin = vec(head.match(/"origin"\s+"vector3"\s+"([^"]+)"/)?.[1]) ?? [0, 0, 0];
  let keep = true;
  switch (cls) {
    case "func_brush": keep = name.startsWith("Counter_digit_") ? true : /^(ct|t)_brush_/.test(name) ? false : onPitch(pts); break;
    case "func_physbox": case "func_button": case "trigger_teleport": case "info_teleport_destination": case "func_reflective_glass": case "func_ladder": keep = false; break;
    case "trigger_once": keep = name === "ct_But" || name === "terro_But"; break;
    case "func_door": keep = name === "doormain"; if (keep) edits.push({ id: idOf(e), what: "door shut", apply: (t) => t.replace('"classname" "string" "func_door"', '"classname" "string" "func_brush"') }); break;
    case "trigger_multiple": keep = onPitch(pts); break;
    case "light_omni2": keep = origin[2] > 600 && inBox(origin); break;
    case "team_select": edits.push({ id: idOf(e), what: "team select on the pitch", apply: (t) => place(t, [25, 190, -25], [0, 0, 0]) }); break;
    case "terrorist_team_intro": edits.push({ id: idOf(e), what: "T intro in the red half", apply: (t) => place(t, [25, 900, -25], [0, 0, 0]) }); break;
    case "counterterrorist_team_intro": edits.push({ id: idOf(e), what: "CT intro in the blue half", apply: (t) => place(t, [-25, -900, -25], [0, 180, 0]) }); break;
    case "env_sky": edits.push({ id: idOf(e), what: "sky", apply: (t) => t.replace(/"skyname" "string" "[^"]*"/, `"skyname" "string" "${SKY_NAME}"`).replace(/"tint_color" "string" "[^"]*"/, '"tint_color" "string" "255 255 255"') }); break;
    default: break;
  }
  if (keep && GOAL_SHIFT && ["func_brush", "trigger_multiple", "trigger_once"].includes(cls) && inGoal(pts.length ? pts : [origin])) {
    const sign = Math.sign((pts.length ? pts : [origin]).reduce((s, p) => s + p[1], 0));
    goalShifts.push({ id: idOf(e), type: "CMapEntity", sign, what: `${cls} ${name}`.trim() });
    for (const m of meshes) { let inline = false; for (let p = m.parent; p; p = p.parent) if (p === e) inline = true; if (!inline) goalShifts.push({ id: idOf(m), type: "CMapMesh", sign, what: `mesh of ${cls}` }); }
  }
  const bucket = keep ? report.entities.kept : report.entities.removed; bucket[cls] = (bucket[cls] ?? 0) + 1;
  if (!keep) { remove.add(e); meshes.forEach((m) => remove.add(m)); }
}
function place(t, origin, angles) {
  return t.replace(/("isProceduralEntity" "bool" "\d"\s*"origin" "vector3" ")[^"]*(")/, `$1${origin.join(" ")}$2`).replace(/("origin" "vector3" "[^"]*"\s*"angles" "qangle" ")[^"]*(")/, `$1${angles.join(" ")}$2`);
}
for (const n of nodes) {
  if (n.type !== "CMapMesh" || entityMeshes.has(n)) continue;
  const mat = material(n), pts = meshPoints(n);
  // the pitch slab (grass on top, same material underneath) and everything standing on the pitch
  const keep = !mat.startsWith("materials/tools/") && (mat === "materials/tm/grass20.vmat" ? pts.every(inBox) : onPitch(pts));
  if (keep) report.worldMeshes.kept[mat] = (report.worldMeshes.kept[mat] ?? 0) + 1; else { remove.add(n); report.worldMeshes.removed++; }
  if (keep && GOAL_SHIFT && inGoal(pts)) goalShifts.push({ id: idOf(n), type: "CMapMesh", sign: Math.sign(pts.reduce((s, p) => s + p[1], 0)), what: `goal frame ${mat.split("/").pop()}` });
}
console.log(JSON.stringify(report, null, 1));
console.log(`edits: ${edits.map((e) => e.what).join(", ")}`);
if (args["dry-run"]) process.exit(0);

// ---- templates (taken before anything is cut) ---------------------------------------------------------------
const elementText = (n) => text.slice(n.tokStart, n.close + 1);
const boxTemplate = (() => { const n = nodes.find((m) => m.type === "CMapMesh" && material(m) === "materials/tools/toolsplayerclip.vmat" && meshPoints(m).length === 8); if (!n) throw new Error("no box mesh to copy"); return elementText(n); })();
const propTemplateNode = entities.find((e) => classOf(e) === "prop_static");
const lightTemplateNode = entities.find((e) => !remove.has(e) && classOf(e) === "light_omni2");
if (!propTemplateNode || !lightTemplateNode) throw new Error("no prop_static / light_omni2 to copy");
let nodeId = Math.max(...[...text.matchAll(/"nodeID"\s+"int"\s+"(\d+)"/g)].map((m) => Number(m[1])));
const fresh = (t) => t.replace(/"elementid" "[0-9a-f-]+"/g, () => `"elementid" "${crypto.randomUUID()}"`).replace(/"nodeID" "int" "\d+"/, () => `"nodeID" "int" "${++nodeId}"`)
  .replace(/"referenceID" "uint64" "0x[0-9a-f]+"/, () => `"referenceID" "uint64" "0x${crypto.randomBytes(8).toString("hex")}"`).replace(/"randomSeed" "int" "\d+"/, () => `"randomSeed" "int" "${crypto.randomInt(1, 2 ** 31 - 1)}"`);
const setProp = (t, key, value) => { const re = new RegExp(`"${key}" "string" "[^"]*"`); if (!re.test(t)) throw new Error(`template has no ${key}`); return t.replace(re, `"${key}" "string" "${value}"`); };
const additions = [];
// the arena
// No dynamic shadows from the stadium: the roof darkened the plugin's design floors and the
// players in the south-west corner, while the map's own floor there is lit (checked in game).
for (const model of MODELS) additions.push(setProp(setProp(fresh(elementText(propTemplateNode)), "model", model), "disableshadows", "1"));
// sky box: six slabs round the stadium
{
  const { x, y, z0, z1 } = layout.sky, t = 32;
  const slabs = [[-x - t, -y - t, z0 - t, x + t, y + t, z0], [-x - t, -y - t, z1, x + t, y + t, z1 + t], [-x - t, -y - t, z0, -x, y + t, z1], [x, -y - t, z0, x + t, y + t, z1], [-x, -y - t, z0, x, -y, z1], [-x, y, z0, x, y + t, z1]];
  for (const [x0, y0, zz0, x1, y1, zz1] of slabs) {
    // corner order of the template box: (x0 y1 z1) (x0 y1 z0) (x0 y0 z0) (x0 y0 z1) (x1 y0 z0) (x1 y0 z1) (x1 y1 z0) (x1 y1 z1)
    const corners = [[x0, y1, zz1], [x0, y1, zz0], [x0, y0, zz0], [x0, y0, zz1], [x1, y0, zz0], [x1, y0, zz1], [x1, y1, zz0], [x1, y1, zz1]];
    let k = 0;
    const box = fresh(boxTemplate).replace("materials/tools/toolsplayerclip.vmat", SKY_MATERIAL)
      .replace(/("standardAttributeName" "string" "position"[\s\S]*?"data" "vector3_array"\s*\[)([\s\S]*?)(\])/, (_, a, body, c) => a + body.replace(/"[^"]+"/g, () => `"${corners[k++].join(" ")}"`) + c);
    if (k !== 8) throw new Error("box template: expected 8 corners");
    additions.push(box);
  }
}
// fill lights (baked): under the roof at every truss, under the upper stand at every block
const lights = [];
{
  const st = layout.stations, at = (i, d, z) => [st[i].x + st[i].nx * d, st[i].y + st[i].ny * d, z];
  for (const i of layout.roof.trussStations) lights.push({ origin: at(i, 820, st[i].roofBack - 150), brightness: "0.9", range: "2600" });
  const blocks = new Map(); st.forEach((s, i) => { if (s.block >= 0) { if (!blocks.has(s.block)) blocks.set(s.block, []); blocks.get(s.block).push(i); } });
  for (const list of blocks.values()) { const i = list[Math.floor(list.length / 2)]; lights.push({ origin: at(i, 590, 405), brightness: "0.4", range: "800" }); }
  for (const l of lights) {
    let t = fresh(elementText(lightTemplateNode));
    t = setProp(setProp(setProp(t, "brightness", l.brightness), "range", l.range), "precomputedfieldsvalid", "0");
    t = t.replace(/("isProceduralEntity" "bool" "\d"\s*"origin" "vector3" ")[^"]*(")/, `$1${l.origin.map((v) => v.toFixed(2)).join(" ")}$2`);
    additions.push(t);
  }
}

// ---- rewrite ----------------------------------------------------------------------------------------------------
let out = text;
const cut = [...remove].filter((n) => { for (let p = n.parent; p; p = p.parent) if (remove.has(p)) return false; return true; }).sort((a, b) => b.tokStart - a.tokStart);
for (const n of cut) {
  let start = n.tokStart, end = n.close + 1;
  if (n.parent) {   // inline elements are comma separated
    const after = out.slice(end, end + 40).match(/^\s*,/);
    if (after) end += after[0].length; else { const before = out.slice(Math.max(0, start - 40), start).match(/,\s*$/); if (before) start -= before[0].length; }
  }
  out = out.slice(0, start) + out.slice(end);
}
const goneIds = new Set(cut.filter((n) => !n.parent).map(idOf));
out = out.replace(/^[ \t]*"element" "([0-9a-f-]{36})",?\r?\n/gm, (line, id) => (goneIds.has(id) ? "" : line));
out = out.replace(/,(\s*\])/g, "$1");
// edits on kept entities
for (const e of edits) {
  const at = out.indexOf(`"id" "elementid" "${e.id}"`); if (at < 0) throw new Error(`edit target gone: ${e.what}`);
  const start = out.lastIndexOf('"CMapEntity"', at); let depth = 0, end = -1;
  for (let k = out.indexOf("{", start); k < out.length; k++) { if (out[k] === "{") depth++; else if (out[k] === "}" && --depth === 0) { end = k + 1; break; } }
  const before = out.slice(start, end), after = e.apply(before);
  if (after === before) throw new Error(`edit changed nothing: ${e.what}`);
  out = out.slice(0, start) + after + out.slice(end);
}
// the goals move out to their CS:S places
{
  let moved = 0;
  const shiftPositions = (t, dy) => t.replace(/("standardAttributeName" "string" "position"[\s\S]*?"data" "vector3_array"\s*\[)([\s\S]*?)(\])/g,
    (_, a, body, c) => a + body.replace(/"([^"]+)"/g, (m, val) => { const p = val.trim().split(/\s+/).map(Number); p[1] += dy; moved++; return `"${p.join(" ")}"`; }) + c);
  for (const g of goalShifts) {
    const at = out.indexOf(`"id" "elementid" "${g.id}"`); if (at < 0) throw new Error(`goal part gone: ${g.what}`);
    const start = out.lastIndexOf(`"${g.type}"`, at); let depth = 0, end = -1;
    for (let k = out.indexOf("{", start); k < out.length; k++) { if (out[k] === "{") depth++; else if (out[k] === "}" && --depth === 0) { end = k + 1; break; } }
    const dy = g.sign * GOAL_SHIFT;
    let t = shiftPositions(out.slice(start, end), dy);
    if (g.type === "CMapEntity") {
      const before = t;
      t = t.replace(/("isProceduralEntity" "bool" "\d"\s*"origin" "vector3" ")([^"]*)(")/, (_, a, o, c) => { const p = o.trim().split(/\s+/).map(Number); p[1] += dy; return a + p.join(" ") + c; });
      if (t === before) throw new Error(`goal part has no origin: ${g.what}`);
    }
    out = out.slice(0, start) + t + out.slice(end);
  }
  if (GOAL_SHIFT && moved === 0) throw new Error("goal shift moved no vertex");
  console.log(`goals moved ${GOAL_SHIFT} units out: ${goalShifts.length} elements, ${moved} vertices (${[...new Set(goalShifts.map((g) => g.what))].join(", ")})`);
}
// additions: top-level elements after the prop_static template, listed in the world right after it
{
  const templateId = idOf(propTemplateNode), at = out.indexOf(`"id" "elementid" "${templateId}"`), start = out.lastIndexOf('"CMapEntity"', at);
  let depth = 0, end = -1;
  for (let k = out.indexOf("{", start); k < out.length; k++) { if (out[k] === "{") depth++; else if (out[k] === "}" && --depth === 0) { end = k + 1; break; } }
  out = out.slice(0, end) + "\n\n" + additions.join("\n\n") + out.slice(end);
  const ref = new RegExp(`^([ \\t]*)"element" "${templateId}"(,?)(\\r?\\n)`, "m").exec(out);
  if (!ref) throw new Error("the world does not list the prop_static template");
  const ids = additions.map((t) => t.match(/"id" "elementid" "([0-9a-f-]+)"/)[1]);
  const lines = [templateId, ...ids].map((id, k, all) => `${ref[1]}"element" "${id}"${k < all.length - 1 || ref[2] ? "," : ""}${ref[3]}`).join("");
  out = out.slice(0, ref.index) + lines + out.slice(ref.index + ref[0].length);
}
// the pitch floor gets the high-resolution grass (same squares, same surface, same UVs)
{
  const before = out.split('"materials/tm/grass20.vmat"').length - 1;
  out = out.split('"materials/tm/grass20.vmat"').join('"materials/soccermod_arena/pitch_grass.vmat"');
  console.log(`pitch grass material: ${before} references re-pointed`);
}
// the goal nets get v8's rope net (owner 2026-10-01: "the same net as we have in v8 for both goals")
{
  const old = '"materials/soccermod_stadium/sm_net_white_glass.vmat"', n = out.split(old).length - 1;
  if (!n) throw new Error("no goal net material found");
  out = out.split(old).join('"materials/soccermod_arena/goal_net.vmat"');
  console.log(`goal net material: ${n} references re-pointed`);
}
fs.writeFileSync(args.out, out);
console.log(`wrote ${args.out}: ${text.length} -> ${out.length} bytes; removed ${cut.length} elements, added ${MODELS.length} models, 6 sky slabs, ${lights.length} lights`);
