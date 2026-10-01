#!/usr/bin/env node
// SoccerMod indoor hall: writes the hall as static models (one per part, the pitch and the
// court with their collision), a preview scene for the local viewer and the layout file that the
// crowd generator, the map builder and the radar read.
//
//   node tools/hall/generate-hall.mjs [--addon <content addon dir>] [--preview <dir>] [--layout <file>]
import fs from "node:fs";
import path from "node:path";
import { buildHall } from "./geometry.mjs";
import * as L from "./layout.mjs";
import { staticDmx, staticVmdl } from "../arena/lib/dmx.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
const { scene, crowd, lights } = buildHall();
const drawn = scene.faces.filter((f) => !f.physOnly);
console.log(`${scene.faces.length} faces (${drawn.length} drawn), ${drawn.reduce((s, f) => s + f.pts.length - 2, 0)} triangles, ${new Set(drawn.map((f) => f.material)).size} materials`);

// ---- models ---------------------------------------------------------------------------------------
export const MODEL_DIR = "models/soccermod_hall";
// part -> physics surface (none: the part is only drawn)
const MAP_PARTS = { pitch: "grass", court: "concrete", shell: null, west: null, east: null, ends: null, roof: null };
// the plugin spawns these (they are not placed in the map): one floor per pitch design
const PLUGIN_PARTS = Object.fromEntries(L.DESIGNS.map((d) => [`design_${d}`, null]));
const PARTS = { ...MAP_PARTS, ...PLUGIN_PARTS };
if (args.addon) {
  const dir = path.join(args.addon, MODEL_DIR);
  fs.mkdirSync(dir, { recursive: true });
  for (const [part, surface] of Object.entries(PARTS)) {
    const name = `hall_${part}`, faces = drawn.filter((f) => f.group === part), phys = scene.faces.filter((f) => f.group === part && f.solid);
    if (!faces.length) throw new Error(`no faces in part ${part}`);
    fs.writeFileSync(path.join(dir, `${name}.dmx`), staticDmx(name, faces));
    if (surface) fs.writeFileSync(path.join(dir, `${name}_phys.dmx`), staticDmx(`${name}_phys`, phys.map((f) => ({ ...f, material: "materials/soccermod_hall/collide.vmat" }))));
    fs.writeFileSync(path.join(dir, `${name}.vmdl`), staticVmdl(MODEL_DIR, name, !!surface, surface ?? "concrete"));
    console.log(`${MODEL_DIR}/${name}: ${faces.length} faces${surface ? `, ${phys.length} collision faces (${surface})` : ""}`);
  }
}

// ---- preview scene for the local viewer -----------------------------------------------------------
if (args.preview) {
  fs.mkdirSync(args.preview, { recursive: true });
  const groups = new Map();
  for (const f of drawn) {
    if (!groups.has(f.material)) groups.set(f.material, { data: [], twoSided: false });
    const g = groups.get(f.material); g.twoSided ||= f.twoSided;
    for (let k = 1; k + 1 < f.pts.length; k++) for (const v of [0, k, k + 1]) g.data.push(...f.pts[v], ...f.n, ...f.uvs[v]);
  }
  const manifest = { materials: [], lights: lights.map((l) => ({ at: l.at, brightness: l.brightness, range: l.range, color: l.color })), floor: L.FLOOR };
  const chunks = []; let offset = 0;
  for (const [material, g] of groups) {
    const buf = Buffer.from(new Float32Array(g.data).buffer);
    manifest.materials.push({ name: path.basename(material, ".vmat"), offset, vertices: g.data.length / 8, twoSided: g.twoSided });
    chunks.push(buf); offset += buf.length;
  }
  fs.writeFileSync(path.join(args.preview, "scene.bin"), Buffer.concat(chunks));
  fs.writeFileSync(path.join(args.preview, "scene.json"), JSON.stringify(manifest));
  console.log(`preview: ${manifest.materials.length} materials, ${(offset / 1048576).toFixed(1)} MB`);
}

// ---- layout ------------------------------------------------------------------------------------------
if (args.layout) {
  const r3 = (v) => Number(v.toFixed(3));
  const layout = {
    note: "SoccerMod indoor hall. Origin = centre spot, +y = red end, floor z = FLOOR. Crowd rows: fans stand between a and b at height h above the floor, looking along `look`.",
    map: L.MAP_NAME, floor: L.FLOOR, pitch: L.PITCH, goal: L.GOAL, board: L.BOARD, net: L.NET, hall: L.HALL,
    models: Object.keys(MAP_PARTS).map((p) => `${MODEL_DIR}/hall_${p}.vmdl`),
    pluginModels: Object.keys(PLUGIN_PARTS).map((p) => `${MODEL_DIR}/hall_${p}.vmdl`),
    lines: L.pitchLines().map((poly) => poly.map((p) => p.map(r3))),
    crowd: crowd.map((r) => ({ ...r, a: r.a.map(r3), b: r.b.map(r3) })),
    lights: lights.map((l) => ({ ...l, at: l.at.map(r3) })),
    boardLine: L.boardLine(0).pts.map((p) => [r3(p.x), r3(p.y)]),
  };
  fs.mkdirSync(path.dirname(args.layout), { recursive: true });
  fs.writeFileSync(args.layout, JSON.stringify(layout, null, 1));
  console.log(`layout: ${args.layout}`);
}
