#!/usr/bin/env node
// SoccerMod street arena: writes the map as static models (court and cage carry the collision), a preview
// scene for the local viewer and the layout file that the crowd generator and the map builder
// (tools/hall/generate-hall-crowd.mjs, tools/hall/apply-hall-vmap.mjs) read.
//
//   node tools/street/generate-street.mjs [--addon <content addon dir>] [--preview <dir>] [--layout <file>]
import fs from "node:fs";
import path from "node:path";
import { buildStreet } from "./geometry.mjs";
import * as L from "./layout.mjs";
import { staticDmx, staticVmdl } from "../arena/lib/dmx.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
const { scene, crowd, lights } = buildStreet();
const drawn = scene.faces.filter((f) => !f.physOnly);
console.log(`${scene.faces.length} faces (${drawn.length} drawn), ${drawn.reduce((s, f) => s + f.pts.length - 2, 0)} triangles, ${new Set(drawn.map((f) => f.material)).size} materials`);

// ---- models ---------------------------------------------------------------------------------------
export const MODEL_DIR = "models/soccermod_street";
// part -> physics surface (none: the part is only drawn)
const PARTS = { court: "concrete", cage: "concrete", west: null, east: null, north: null, south: null, props: null, far: null };
if (args.addon) {
  const dir = path.join(args.addon, MODEL_DIR);
  fs.mkdirSync(dir, { recursive: true });
  for (const [part, surface] of Object.entries(PARTS)) {
    const name = `street_${part}`, faces = drawn.filter((f) => f.group === part), phys = scene.faces.filter((f) => f.group === part && f.solid);
    if (!faces.length) throw new Error(`no faces in part ${part}`);
    fs.writeFileSync(path.join(dir, `${name}.dmx`), staticDmx(name, faces));
    if (surface) fs.writeFileSync(path.join(dir, `${name}_phys.dmx`), staticDmx(`${name}_phys`, phys.map((f) => ({ ...f, material: "materials/soccermod_street/collide.vmat" }))));
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
    note: "SoccerMod street arena (3v3). Origin = centre spot, +y = red end, floor z = FLOOR. Crowd rows: onlookers stand between a and b at height h above the floor, looking along `look`.",
    map: L.MAP_NAME, floor: L.FLOOR, court: L.COURT, goal: L.GOAL,
    // read by tools/hall/apply-hall-vmap.mjs: the sky box round the block, spawns, team select, the onlookers
    hall: { x0: L.BOUNDS.x0, x1: L.BOUNDS.x1, y: Math.max(-L.BOUNDS.y0, L.BOUNDS.y1), ridge: L.BOUNDS.top },
    spawn: { x0: -270, dx: 180, y0: 360, dy: 190 },
    teamSelect: { select: 330, intro: 600 },
    // golden hour: a low sun from the west-north-west over the shops, warm; the sky fills the shadows
    sun: { brightness: 1.5, sky: 0.9, angles: [28, -18], color: "255 184 120", skycolor: "156 180 236" },
    sky: "materials/soccermod_street/sky_sunset.vmat",
    probe: { mins: [L.BOUNDS.x0, L.BOUNDS.y0, L.FLOOR], maxs: [L.BOUNDS.x1, L.BOUNDS.y1, L.FLOOR + 1150], voxel: 96 },
    models: Object.keys(PARTS).map((p) => `${MODEL_DIR}/street_${p}.vmdl`),
    props: [{ name: "sm_street_train", model: `${MODEL_DIR}/train.vmdl`, anim: "run" }, { name: "sm_street_canopy", model: `${MODEL_DIR}/canopy.vmdl`, anim: "sway" }],
    crowdModels: ["east", "west", "end_red", "end_blue"].map((n) => ({ name: n, model: `${MODEL_DIR}/crowd_${n}.vmdl` })),
    crowd: crowd.map((r) => ({ ...r, a: r.a.map(r3), b: r.b.map(r3) })),
    lights: lights.map((l) => ({ ...l, at: l.at.map(r3) })),
  };
  fs.mkdirSync(path.dirname(args.layout), { recursive: true });
  fs.writeFileSync(args.layout, JSON.stringify(layout, null, 1));
  console.log(`layout: ${args.layout}`);
}
