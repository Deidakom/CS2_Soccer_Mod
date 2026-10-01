#!/usr/bin/env node
// SoccerMod Arena: writes the stadium as static models, a preview scene and the layout file
// that the crowd generator and the plugin read.
//
//   node tools/arena/generate-arena.mjs [--addon <content addon dir>] [--preview <dir>] [--layout <file>]
import fs from "node:fs";
import path from "node:path";
import { buildArena, SKY, VIDEO_WALL } from "./geometry.mjs";
import * as L from "./layout.mjs";
import { staticDmx, staticVmdl } from "./lib/dmx.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
const { scene, screens, tunnels } = buildArena();
const stats = scene.stats();
const tris = Object.values(stats).reduce((s, e) => s + e.tris, 0);
console.log(`${scene.faces.length} faces, ${tris} triangles, ${Object.keys(stats).length} materials`);
if (args.stats) console.log(Object.fromEntries(Object.entries(stats).map(([k, v]) => [path.basename(k, ".vmat"), v.tris])));

// ---- models: eight sectors round the pitch -----------------------------------------------------
export const MODEL_DIR = "models/soccermod_arena";
const SECTORS = 8;
const sectorOf = (f) => {
  const c = f.pts.reduce((s, p) => [s[0] + p[0], s[1] + p[1]], [0, 0]);
  return Math.floor(((Math.atan2(c[1], c[0]) + 2 * Math.PI) % (2 * Math.PI)) / (2 * Math.PI / SECTORS)) % SECTORS;
};
if (args.addon) {
  const dir = path.join(args.addon, MODEL_DIR);
  fs.mkdirSync(dir, { recursive: true });
  const bySector = Array.from({ length: SECTORS }, () => []);
  for (const f of scene.faces) bySector[sectorOf(f)].push(f);
  bySector.forEach((faces, s) => {
    const name = `arena_${s}`, solid = faces.filter((f) => f.solid);
    fs.writeFileSync(path.join(dir, `${name}.dmx`), staticDmx(name, faces));
    fs.writeFileSync(path.join(dir, `${name}_phys.dmx`), staticDmx(`${name}_phys`, solid.map((f) => ({ ...f, material: "materials/soccermod_arena/concrete.vmat" }))));
    fs.writeFileSync(path.join(dir, `${name}.vmdl`), staticVmdl(MODEL_DIR, name, true));
    console.log(`${MODEL_DIR}/${name}: ${faces.length} faces (${solid.length} solid)`);
  });
}

// ---- preview scene for the local viewer -----------------------------------------------------------
if (args.preview) {
  fs.mkdirSync(args.preview, { recursive: true });
  const groups = new Map();
  for (const f of scene.faces) {
    if (!groups.has(f.material)) groups.set(f.material, { data: [], twoSided: false });
    const g = groups.get(f.material); g.twoSided ||= f.twoSided;
    for (let k = 1; k + 1 < f.pts.length; k++) for (const v of [0, k, k + 1]) g.data.push(...f.pts[v], ...f.n, ...f.uvs[v]);
  }
  const manifest = { materials: [], sky: SKY, pitch: L.PITCH };
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

// ---- layout for the crowd generator and the plugin --------------------------------------------------
if (args.layout) {
  const r4 = (v) => Number(v.toFixed(3));
  const stations = L.RING.stations.map((s, i) => ({ x: r4(s.x), y: r4(s.y), nx: r4(s.nx), ny: r4(s.ny), S: r4(s.S), kind: L.RING.kinds[i], block: L.RING.blockOf[i], roofBack: r4(L.roofBack(i)) }));
  const ringAt = (d, z) => L.RING.stations.map((_, i) => { const p = L.P(i, d, typeof z === "function" ? z(i) : z); return p.map(r4); });
  const layout = {
    note: "SoccerMod Arena layout. A point is station + d (outwards along the station normal) + z. Stations run counter-clockwise from the middle of the east side (+x); +y is the red end.",
    map: L.MAP_NAME,
    pitch: L.PITCH, curve: L.CURVE, perimeter: r4(L.RING.perimeter), stations,
    tiers: {
      lower: { rows: L.LOWER.rows, depth: L.LOWER.depth, rise: L.LOWER.rise, firstTreadZ: L.lowerRow(0).z, firstTreadD: L.lowerRow(0).d0 },
      upper: { rows: L.UPPER.rows, depth: L.UPPER.depth, rise: L.UPPER.rise, firstTreadZ: L.upperRow(0).z, firstTreadD: L.upperRow(0).d0, vomitoryRows: L.UPPER.vomitoryRows },
    },
    seatWidth: L.SEAT.width, aisleWidth: L.AISLE,
    parapet: { d: 0, top: L.PARAPET.top, adLow: L.PARAPET.adLow, adHigh: L.PARAPET.adHigh },
    ledRing: { d: L.FASCIA.d, zLow: L.FASCIA.ledLow, zHigh: L.FASCIA.ledHigh, points: ringAt(L.FASCIA.d - 1, (L.FASCIA.ledLow + L.FASCIA.ledHigh) / 2) },
    roof: { innerEdge: ringAt(L.roofD(1), (i) => L.roofZ(i, 1)), eave: ringAt(L.roofD(0), (i) => L.roofZ(i, 0)), glassFrom: L.ROOF.glassFrom, trussStations: L.TRUSS_STATIONS },
    screens: screens.map((s) => ({ end: s.end, score: s.score.map(r4), scoreX: s.scoreX, centre: s.centre.map(r4), normal: s.normal.map(r4), right: s.right.map(r4), up: s.up.map(r4), width: s.width, height: s.height })),
    tunnels: tunnels.map((t) => ({ side: t.side, door: t.door, centre: t.centre.map(r4) })),
    sky: SKY, videoWall: VIDEO_WALL,
  };
  fs.mkdirSync(path.dirname(args.layout), { recursive: true });
  fs.writeFileSync(args.layout, JSON.stringify(layout, null, 1));
  console.log(`layout: ${args.layout}`);
}
