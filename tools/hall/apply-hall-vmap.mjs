#!/usr/bin/env node
// SoccerMod indoor hall: writes maps/soccer_indoor_hall.vmap. The stadium's vmap is only the
// source of element templates (an entity of each kind, a box mesh); everything of that map goes,
// then the hall is put in: its models (prop_static, two of them with collision), the fans
// (prop_dynamic, idle clip), lights, spawns, the ball, team select and a sky box round the hall.
//
//   node --max-old-space-size=6144 tools/hall/apply-hall-vmap.mjs --in <soccer_soccermod_arena.vmap> --layout <hall-layout.json> --out <vmap>
import fs from "node:fs";
import crypto from "node:crypto";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.in || !args.layout || !args.out) throw new Error("usage: --in <vmap> --layout <json> --out <vmap>");
const layout = JSON.parse(fs.readFileSync(args.layout, "utf8")), F = layout.floor;
const text = fs.readFileSync(args.in, "utf8");
const SKY_MATERIAL = "materials/tools/toolsskybox.vmat";
const SPAWNS_PER_TEAM = 8;

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
const material = (n) => text.slice(n.open, n.close + 1).match(/"materials"\s+"string_array"\s*\[\s*"([^"]+)"/)?.[1] ?? "";
const pointCount = (n) => [...(text.slice(n.open, n.close + 1).match(/"standardAttributeName"\s+"string"\s+"position"[\s\S]*?"data"\s+"vector3_array"\s*\[([\s\S]*?)\]/)?.[1] ?? "").matchAll(/"[^"]+"/g)].length;
function childMeshes(n) {
  const out = n.kids.filter((k) => k.type === "CMapMesh");
  const list = own(n).match(/"children"\s+"element_array"\s*\[([\s\S]*?)\]/)?.[1] ?? "";
  for (const m of list.matchAll(/"element"\s+"([0-9a-f-]+)"/g)) { const c = byId.get(m[1]); if (c?.type === "CMapMesh") out.push(c); }
  return out;
}
const entities = nodes.filter((n) => n.type === "CMapEntity");
const propsOf = (e) => { const pk = e.kids.find((k) => k.type === "EditGameClassProps"); return pk ? text.slice(pk.open, pk.close + 1) : ""; };
const classOf = (e) => propsOf(e).match(/"classname"\s+"string"\s+"([^"]+)"/)?.[1] ?? "?";
const elementText = (n) => text.slice(n.tokStart, n.close + 1);

// ---- what stays: one of each kind we need, as it is or as a template --------------------------------
const remove = new Set(), edits = [], kept = {}, entityMeshes = new Set();
const place = (t, origin, angles) => {
  const a = t.replace(/("isProceduralEntity" "bool" "\d"\s*"origin" "vector3" ")[^"]*(")/, `$1${origin.join(" ")}$2`);
  if (a === t) throw new Error("entity has no origin to move");
  return a.replace(/("origin" "vector3" "[^"]*"\s*"angles" "qangle" ")[^"]*(")/, `$1${angles.join(" ")}$2`);
};
const setProp = (t, key, value) => { const re = new RegExp(`"${key}" "string" "[^"]*"`); if (!re.test(t)) throw new Error(`template has no ${key}`); return t.replace(re, `"${key}" "string" "${value}"`); };
// spawn grid (4 columns), team select and intro spots: the hall's unless the layout names its own
const SPAWN = layout.spawn ?? { x0: -240, dx: 160, y0: 430, dy: 170 }, SELECT = layout.teamSelect ?? { select: 160, intro: 640 };
const spawnSpot = (k, side) => { const col = k % 4, row = Math.floor(k / 4); return [[SPAWN.x0 + col * SPAWN.dx, side * (SPAWN.y0 + row * SPAWN.dy), F + 1], [0, side > 0 ? 270 : 90, 0]]; };
const count = {};
for (const e of entities) {
  const cls = classOf(e), n = (count[cls] = (count[cls] ?? 0) + 1), id = idOf(e);
  childMeshes(e).forEach((m) => entityMeshes.add(m));
  let keep = false;
  switch (cls) {
    case "prop_physics_multiplayer": keep = n === 1; if (keep) edits.push({ id, what: "ball on the centre spot", apply: (t) => place(setProp(t, "parentname", ""), [0, 0, F + 19], [0, 0, 0]) }); break;
    case "game_player_equip": case "point_servercommand": case "logic_auto": case "env_sky": keep = n === 1; break;
    case "team_select": keep = true; edits.push({ id, what: "team select on the pitch", apply: (t) => place(t, [0, SELECT.select, F + 7], [0, 0, 0]) }); break;
    case "terrorist_team_intro": keep = true; edits.push({ id, what: "T intro in the red half", apply: (t) => place(t, [0, SELECT.intro, F + 7], [0, 0, 0]) }); break;
    case "counterterrorist_team_intro": keep = true; edits.push({ id, what: "CT intro in the blue half", apply: (t) => place(t, [0, -SELECT.intro, F + 7], [0, 180, 0]) }); break;
    case "info_player_terrorist": keep = n <= SPAWNS_PER_TEAM; if (keep) { const [o, a] = spawnSpot(n - 1, 1); edits.push({ id, what: "T spawn", apply: (t) => place(t, o, a) }); } break;
    case "info_player_counterterrorist": keep = n <= SPAWNS_PER_TEAM; if (keep) { const [o, a] = spawnSpot(n - 1, -1); edits.push({ id, what: "CT spawn", apply: (t) => place(t, o, a) }); } break;
    // No sun left everything that moves black (ball, players, the fans): baked lamps do not light
    // dynamic objects here. So the hall keeps a soft "sun" from almost straight above plus sky light as
    // a stand-in for the lamps: it lights what moves and puts the ball's shadow under it. The hall's
    // models cast no shadows (disableshadows), so the roof does not block it (owner 2026-10-01).
    case "light_environment": keep = n === 1; if (keep) edits.push({ id, what: "top light for what moves", apply: (t) => place(setProp(setProp(t, "brightness", String(layout.topLight?.brightness ?? 0.65)), "skyintensity", String(layout.topLight?.sky ?? 0.6)), [0, 0, F + 1200], [84, 30, 0]) }); break;
    case "prop_static": keep = n === 1; if (keep) kept.prop = e; break;
    case "light_omni2": keep = n === 1; if (keep) kept.light = e; break;
    default: break;
  }
  if (!keep) { remove.add(e); childMeshes(e).forEach((m) => remove.add(m)); }
}
if (!kept.prop || !kept.light) throw new Error("no prop_static / light_omni2 to copy");
for (const need of ["prop_physics_multiplayer", "team_select", "info_player_terrorist", "info_player_counterterrorist"]) if (!count[need]) throw new Error(`the source map has no ${need}`);
if (count.info_player_terrorist < SPAWNS_PER_TEAM || count.info_player_counterterrorist < SPAWNS_PER_TEAM) throw new Error("not enough spawns in the source map");
// every world mesh goes; one box of the old sky box is the template for the new one
let boxTemplate = null;
for (const n of nodes) {
  if (n.type !== "CMapMesh" || entityMeshes.has(n)) continue;
  if (!boxTemplate && material(n) === SKY_MATERIAL && pointCount(n) === 8) boxTemplate = elementText(n);
  remove.add(n);
}
if (!boxTemplate) throw new Error("no box mesh to copy");

// ---- what comes in -------------------------------------------------------------------------------------
let nodeId = Math.max(...[...text.matchAll(/"nodeID"\s+"int"\s+"(\d+)"/g)].map((m) => Number(m[1])));
const fresh = (t) => t.replace(/"elementid" "[0-9a-f-]+"/g, () => `"elementid" "${crypto.randomUUID()}"`).replace(/"nodeID" "int" "\d+"/, () => `"nodeID" "int" "${++nodeId}"`)
  .replace(/"referenceID" "uint64" "0x[0-9a-f]+"/, () => `"referenceID" "uint64" "0x${crypto.randomBytes(8).toString("hex")}"`).replace(/"randomSeed" "int" "\d+"/, () => `"randomSeed" "int" "${crypto.randomInt(1, 2 ** 31 - 1)}"`);
const propText = elementText(kept.prop), lightText = elementText(kept.light), additions = [];
// the hall's parts; the first one reuses the template entity itself
const [firstModel, ...otherModels] = layout.models;
edits.push({ id: idOf(kept.prop), what: "first hall model", apply: (t) => setProp(setProp(t, "model", firstModel), "disableshadows", "1") });
for (const model of otherModels) additions.push(setProp(setProp(fresh(propText), "model", model), "disableshadows", "1"));
// the fans: prop_dynamic with the idle clip (the plugin can switch the clip by targetname)
// crowd models: { name: targetname suffix, model }; the hall's four stands unless the layout lists its own
const CROWD = layout.crowdModels ?? ["west", "east", "end_red", "end_blue"].map((n) => ({ name: n, model: `models/soccermod_hall/crowd_${n}.vmdl` }));
// an entity of another class from the prop_static template: its properties are replaced by `props` ({ key: value })
const entityFrom = (props) => {
  const t = fresh(propText), a = t.indexOf('"entity_properties" "EditGameClassProps"'), open = t.indexOf("{", a);
  let depth = 0, close = -1; for (let k = open; k < t.length; k++) { if (t[k] === "{") depth++; else if (t[k] === "}" && --depth === 0) { close = k; break; } }
  const id = t.slice(open, close).match(/"id" "elementid" "[0-9a-f-]+"/)[0], ind = "\t\t";
  return t.slice(0, open) + "{\n" + [id, ...Object.entries(props).map(([k, v]) => `"${k}" "string" "${v}"`)].map((p) => ind + p).join("\n") + "\n\t" + t.slice(close);
};
for (const { name, model: crowdModel } of CROWD)
  additions.push(entityFrom({ classname: "prop_dynamic", targetname: `sm_hall_crowd_${name}`, model: crowdModel, DefaultAnim: "idle", solid: "0", disableshadows: "1", rendercolor: "255 255 255", skin: "default", StartDisabled: "0" }));
// A light probe volume (layout.probe = { mins, maxs, voxel } in world units): the baked lamps' light
// and a reflection cube map for what moves and for glossy floors. Keys as Hammer writes them
// (content_examples/lighting_info.vmap).
if (layout.probe) {
  const { mins, maxs, voxel = 48 } = layout.probe, c = mins.map((v, k) => (v + maxs[k]) / 2), f = (v) => v.map((x) => x.toFixed(6)).join(" ");
  additions.push(place(entityFrom({ classname: "env_combined_light_probe_volume", targetname: "", vscripts: "", parentname: "", parentAttachmentName: "", "local.origin": "", "local.angles": "", "local.scales": "", useLocalOffset: "0",
    StartDisabled: "0", cubemaptexture: "", bakenearz: "2.0", bakefarz: "4096.0", lightgroup: "", moveable: "0", lightprobetexture: "",
    box_mins: f(mins.map((v, k) => v - c[k])), box_maxs: f(maxs.map((v, k) => v - c[k])), voxel_size: voxel.toFixed(1), flood_fill: "1", voxelize: "1",
    light_probe_volume_from_cubemap: "0", storage: "-1", indoor_outdoor_level: "0", edge_fade_dists: "0 0 0", clientSideEntity: "1" }), c, [0, 0, 0]));
}
// sky box: six slabs round the hall (nothing of it is seen from the court; spectators outside see sky, not void)
{
  const x0 = layout.hall.x0 - 320, x1 = layout.hall.x1 + 320, y = layout.hall.y + 320, z0 = F - 160, z1 = F + layout.hall.ridge + 260, t = 32;
  const slabs = [[x0 - t, -y - t, z0 - t, x1 + t, y + t, z0], [x0 - t, -y - t, z1, x1 + t, y + t, z1 + t], [x0 - t, -y - t, z0, x0, y + t, z1], [x1, -y - t, z0, x1 + t, y + t, z1], [x0, -y - t, z0, x1, -y, z1], [x0, y, z0, x1, y + t, z1]];
  for (const [ax, ay, az, bx, by, bz] of slabs) {
    // corner order of the template box: (x0 y1 z1) (x0 y1 z0) (x0 y0 z0) (x0 y0 z1) (x1 y0 z0) (x1 y0 z1) (x1 y1 z0) (x1 y1 z1)
    const corners = [[ax, by, bz], [ax, by, az], [ax, ay, az], [ax, ay, bz], [bx, ay, az], [bx, ay, bz], [bx, by, az], [bx, by, bz]];
    let k = 0;
    const box = fresh(boxTemplate).replace(/("standardAttributeName" "string" "position"[\s\S]*?"data" "vector3_array"\s*\[)([\s\S]*?)(\])/, (_, a, body, c) => a + body.replace(/"[^"]+"/g, () => `"${corners[k++].join(" ")}"`) + c);
    if (k !== 8) throw new Error("box template: expected 8 corners");
    additions.push(box);
  }
}
// lights (baked); the first reuses the template entity
const lightEdit = (t, l) => {
  let o = setProp(setProp(setProp(t, "brightness", String(l.brightness)), "range", String(l.range)), "precomputedfieldsvalid", "0");
  o = setProp(o, "color", l.color ?? "255 255 255");
  return o.replace(/("isProceduralEntity" "bool" "\d"\s*"origin" "vector3" ")[^"]*(")/, `$1${l.at.map((v) => v.toFixed(2)).join(" ")}$2`);
};
const [firstLight, ...otherLights] = layout.lights;
edits.push({ id: idOf(kept.light), what: "first light", apply: (t) => lightEdit(t, firstLight) });
for (const l of otherLights) additions.push(lightEdit(fresh(lightText), l));

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
const entityRange = (id, what) => {
  const at = out.indexOf(`"id" "elementid" "${id}"`); if (at < 0) throw new Error(`edit target gone: ${what}`);
  const start = out.lastIndexOf('"CMapEntity"', at); let depth = 0, end = -1;
  for (let k = out.indexOf("{", start); k < out.length; k++) { if (out[k] === "{") depth++; else if (out[k] === "}" && --depth === 0) { end = k + 1; break; } }
  return [start, end];
};
for (const e of edits) {
  const [start, end] = entityRange(e.id, e.what), before = out.slice(start, end), after = e.apply(before);
  if (after === before) throw new Error(`edit changed nothing: ${e.what}`);
  out = out.slice(0, start) + after + out.slice(end);
}
// additions: top-level elements after the prop_static template, listed in the world right after it
{
  const templateId = idOf(kept.prop), [, end] = entityRange(templateId, "prop template");
  out = out.slice(0, end) + "\n\n" + additions.join("\n\n") + out.slice(end);
  const ref = new RegExp(`^([ \\t]*)"element" "${templateId}"(,?)(\\r?\\n)`, "m").exec(out);
  if (!ref) throw new Error("the world does not list the prop_static template");
  const ids = additions.map((t) => t.match(/"id" "elementid" "([0-9a-f-]+)"/)[1]);
  const lines = [templateId, ...ids].map((id, k, all) => `${ref[1]}"element" "${id}"${k < all.length - 1 || ref[2] ? "," : ""}${ref[3]}`).join("");
  out = out.slice(0, ref.index) + lines + out.slice(ref.index + ref[0].length);
}
fs.writeFileSync(args.out, out);
const keptCount = Object.entries(count).map(([c, n]) => `${c} ${n}`).join(", ");
console.log(`source entities: ${keptCount}`);
console.log(`wrote ${args.out}: ${text.length} -> ${out.length} bytes; removed ${cut.length} elements; ${layout.models.length} models, ${CROWD.length} crowd models, ${layout.lights.length} lights, 6 sky slabs, ${2 * SPAWNS_PER_TEAM} spawns`);
