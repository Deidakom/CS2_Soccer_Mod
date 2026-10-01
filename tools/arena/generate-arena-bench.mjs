#!/usr/bin/env node
// SoccerMod stadium: bench and coach in the same look as the fans (owner 2026-10-01: "the coach
// and bench are not the same model as the fans, fix that"). The v8 set (tools/atmo/
// generate-dugouts.mjs) is boxy 3D figures; here they are cut-out cards from the fan atlas
// (lib/fans.mjs, material materials/soccermod_arena/crowd.vmat), like every fan in the stands.
//
//   models/soccermod/atmo/crowd_arena/bench_red.vmdl    six substitutes on the north bench (map coordinates)
//   models/soccermod/atmo/crowd_arena/bench_blue.vmdl   the same on the south bench
//   models/soccermod/atmo/crowd_arena/coach_red.vmdl    standing coach, origin at the feet, facing +x
//   models/soccermod/atmo/crowd_arena/coach_blue.vmdl
// Clips as in the v8 set, so the plugin (AtmoDugouts.cs) needs nothing but the other model names:
// idle (breathing) and celebrate (the substitutes get up in front of the bench and jump, the coach jumps).
//
// The substitutes sit: a card is sunk until its hips are on the seat (top z -13), the bench slab
// hides the thighs and the floor the feet, so torso above and shins below the bench are seen.
//
// usage: node tools/arena/generate-arena-bench.mjs <content addon dir>
import fs from "node:fs";
import path from "node:path";
import { meshDmx, animDmx, vmdlText } from "../atmo/dmx-lib.mjs";
import { FAN_ATLAS } from "./lib/fans.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-arena-bench.mjs <addon content dir>"); process.exit(1); }
const SOURCE = "tools/arena/generate-arena-bench.mjs";
const DIR = "models/soccermod/atmo/crowd_arena";
const MAT = "materials/soccermod_arena/crowd.vmat";
const FPS = 30, A = FAN_ATLAS;
// bench (geometry.mjs, DUGOUTS): seat top z -13, hips at x -1558, six seats 38 units apart from |y| 248
const SEAT_Z = -13, HIP_X = -1557, SEATS = 6, SEAT_STEP = 38, FIRST_Y = 248;
const SUB_H = 64, SUB_W = 32, COACH_H = 72, COACH_W = 36;
const HIP = (6 + 104) / A.cellH;             // hip joint of a drawn figure, as a share of the card height

let seed = 20261002;
const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
// atlas rows: 0 red, 2 blue (arms down). Columns 0..13 wear the team shirt, 14 and 15 a neutral one.
const rowOf = (team) => (team === "red" ? 0 : 2);

function card(mesh, bone, [cx, cy, z], w, h, col, row, turn) {
  // facing +x (towards the pitch), turned a little; same winding as the fans
  const ct = Math.cos(turn), st = Math.sin(turn), rx = st, ry = ct, nx = ct, ny = -st;
  const u0 = col / A.cols, u1 = (col + 1) / A.cols, v0 = row * A.cellH / A.size, v1 = (row + 1) * A.cellH / A.size;
  const base = mesh.positions.length;
  for (const [dr, dz, u, v] of [[-w / 2, 0, u0, v1], [w / 2, 0, u1, v1], [w / 2, h, u1, v0], [-w / 2, h, u0, v0]]) {
    mesh.positions.push([cx + rx * dr, cy + ry * dr, z + dz]); mesh.normals.push([nx, ny, 0]); mesh.uvs.push([u, v]);
    mesh.weights.push([1, 0]); mesh.indices.push([bone, 0]);
  }
  mesh.faces.push([base, base + 3, base + 2, base + 1]);
}
const newMesh = (name) => ({ name, material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] });
const clip = (bones, seconds, fn) => {
  const n = Math.round(seconds * FPS), frames = [];
  for (let f = 0; f <= n; f++) { const m = new Map(); for (let b = 1; b < bones.length; b++) m.set(b, fn(f / FPS, b)); frames.push(m); }
  return frames;
};
const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
function save(name, bones, mesh, clips) {
  const model = `${DIR}/${name}`;
  write(`${model}.dmx`, meshDmx(SOURCE, bones, mesh));
  for (const c of clips) write(`${model}_anims/${c.name}.dmx`, animDmx(SOURCE, bones, name, c.name, c.frames, FPS));
  write(`${model}.vmdl`, vmdlText(model, name, [], clips));
  console.log(`${model}: ${mesh.faces.length} figure(s)`);
}

for (const team of ["red", "blue"]) {
  const sign = team === "red" ? 1 : -1, row = rowOf(team);
  // ---- bench: six different team-shirt fans, sitting ----------------------------------------------
  {
    const name = `bench_${team}`, mesh = newMesh(name), bones = [{ name: "root", pos: [0, 0, 0] }];
    // grown-ups in a clear team shirt (looked up in the atlas: no children, no dark jackets)
    const cols = team === "red" ? [1, 4, 5, 7, 8, 10] : [1, 3, 5, 6, 10, 12];
    const phase = [0];
    for (let k = 0; k < SEATS; k++) {
      const y = sign * (FIRST_Y + k * SEAT_STEP), h = SUB_H * (0.95 + rnd() * 0.1), w = SUB_W * (0.95 + rnd() * 0.1);
      bones.push({ name: `sub${k}`, pos: [HIP_X, y, SEAT_Z + 20] }); phase.push(rnd() * Math.PI * 2);
      card(mesh, k + 1, [HIP_X, y, SEAT_Z - HIP * h], w, h, cols[k], row, (rnd() - 0.5) * 0.3);
    }
    // getting up: 14 units forward (in front of the bench slab), feet on the floor, then jumping
    const rise = HIP * SUB_H - (SEAT_Z + 32);
    save(name, bones, mesh, [
      { name: "idle", looping: true, frames: clip(bones, 2.0, (t, b) => [0, 0, 0.5 * Math.sin(2 * Math.PI * t / 2 + phase[b])]) },
      { name: "celebrate", looping: true, frames: clip(bones, 2.0, (t, b) => [14, 0, rise + 16 * Math.abs(Math.sin(Math.PI * 2.5 * t + phase[b]))]) },
    ]);
  }
  // ---- coach: one of the two neutral-shirt figures of the team's row, standing ------------------------
  {
    const name = `coach_${team}`, mesh = newMesh(name), bones = [{ name: "root", pos: [0, 0, 0] }, { name: "coach", pos: [0, 0, 40] }];
    card(mesh, 1, [0, 0, 0], COACH_W, COACH_H, team === "red" ? 14 : 15, row, 0);
    save(name, bones, mesh, [
      { name: "idle", looping: true, frames: clip(bones, 2.0, (t) => [0, 0, 0.5 * Math.sin(2 * Math.PI * t / 2)]) },
      { name: "celebrate", looping: true, frames: clip(bones, 2.0, (t) => [0, 0, 14 * Math.abs(Math.sin(Math.PI * 2.5 * t))]) },
    ]);
  }
}
