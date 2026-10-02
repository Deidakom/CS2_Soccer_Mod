#!/usr/bin/env node
// SoccerMod street arena: the subway train on the elevated line. One model, one bone that carries
// the whole train, one looping clip "run": every 45 seconds the train comes out from under the
// building in the north, rolls along the line over the court's edge and disappears into the hill.
// The map places it as a prop_dynamic with that clip - no plugin needed.
//   models/soccermod_street/train.vmdl   (material materials/soccermod_street/train.vmat, a sheet
//   with both sides, the front, the roof and the underside: generate-street-textures.mjs)
//
// usage: node tools/street/generate-street-train.mjs <content addon dir>
import fs from "node:fs";
import path from "node:path";
import { meshDmx, animDmx, vmdlText } from "../atmo/dmx-lib.mjs";
import { Z, EL } from "./layout.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-street-train.mjs <content addon dir>"); process.exit(1); }
const SOURCE = "tools/street/generate-street-train.mjs", MODEL = "models/soccermod_street/train", MAT = "materials/soccermod_street/train.vmat";
const FPS = 30, LOOP = 45, CARS = 3, CAR = 560, GAP = 12, HALF = 58, H0 = EL.deckH + 14, H1 = EL.deckH + 146, X = EL.trackX;
const LENGTH = CARS * CAR + (CARS - 1) * GAP, HIDE = EL.y + LENGTH / 2 + 120;   // the train's centre when it stands hidden behind a portal

const bones = [{ name: "root", pos: [0, 0, 0] }, { name: "train", pos: [X, 0, 0] }];
const mesh = { name: "train", material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
// a rectangle: corners bottom-left, bottom-right, top-right, top-left as seen from the front; uv = [u0, v0, u1, v1] (v0 = top)
const quad = (bl, br, tr, tl, n, [u0, v0, u1, v1], bone = 1) => {
  const base = mesh.positions.length;
  for (const [p, uv] of [[bl, [u0, v1]], [br, [u1, v1]], [tr, [u1, v0]], [tl, [u0, v0]]]) { mesh.positions.push(p); mesh.normals.push(n); mesh.uvs.push(uv); mesh.weights.push([1, 0]); mesh.indices.push([bone, 0]); }
  mesh.faces.push([base, base + 3, base + 2, base + 1]);
};
// sheet: rows of 256 px in a 2048 x 1024 picture - 0 the court side, 1 the far side, 2 front | roof | underside
const ROW = (r) => [r * 0.25, (r + 1) * 0.25];
for (let c = 0; c < CARS; c++) {
  const y0 = -LENGTH / 2 + c * (CAR + GAP), y1 = y0 + CAR, x0 = X - HALF, x1 = X + HALF, zb = Z(H0), zt = Z(H1);
  const [a0, a1] = ROW(0), [b0, b1] = ROW(1), [c0, c1] = ROW(2);
  // the side towards the court (faces -x: its left is +y) and the far side
  quad([x0, y1, zb], [x0, y0, zb], [x0, y0, zt], [x0, y1, zt], [-1, 0, 0], [c * 0.02, a0, 1 - (2 - c) * 0.02, a1]);
  quad([x1, y0, zb], [x1, y1, zb], [x1, y1, zt], [x1, y0, zt], [1, 0, 0], [0, b0, 1, b1]);
  // the two ends, the roof, the underside with the bogies
  quad([x1, y1, zb], [x0, y1, zb], [x0, y1, zt], [x1, y1, zt], [0, 1, 0], [0, c0, 0.25, c1]);
  quad([x0, y0, zb], [x1, y0, zb], [x1, y0, zt], [x0, y0, zt], [0, -1, 0], [0, c0, 0.25, c1]);
  quad([x0, y0, zt], [x1, y0, zt], [x1, y1, zt], [x0, y1, zt], [0, 0, 1], [0.25, c0, 0.5, c1]);
  quad([x0, y1, zb], [x1, y1, zb], [x1, y0, zb], [x0, y0, zb], [0, 0, -1], [0.5, c0, 0.75, c1]);
  for (const by of [y0 + 90, y1 - 90]) for (const s of [-1, 1]) quad([X + s * 44, by + s * 60, Z(EL.deckH + 4)], [X + s * 44, by - s * 60, Z(EL.deckH + 4)], [X + s * 44, by - s * 60, zb], [X + s * 44, by + s * 60, zb], [s, 0, 0], [0.75, c0, 1, c1]);
}
// two tiny faces far along the line that stay with the root bone: they stretch the model's bounds over the
// whole run, so the train is not culled while it is out of the place it was built in
for (const s of [-1, 1]) { const y = s * (HIDE + LENGTH / 2), z = Z(EL.deckH - 30); quad([X - 1, y, z], [X + 1, y, z], [X + 1, y, z + 1], [X - 1, y, z + 1], [0, -s, 0], [0.6, 0.6, 0.61, 0.61], 0); }

// the run: 1 s hidden in the north, 8 s along the line (easing out of and into the portals), then hidden in the hill
const frames = [], n = LOOP * FPS, T0 = 1, T1 = 9.2;
for (let f = 0; f <= n; f++) {
  const t = f / FPS, k = Math.min(1, Math.max(0, (t - T0) / (T1 - T0))), e = k * k * (3 - 2 * k) * 0.35 + k * 0.65;
  frames.push(new Map([[1, [0, HIDE - 2 * HIDE * e, 0]]]));
}
const clips = [{ name: "run", looping: true, frames }];
const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, meshDmx(SOURCE, bones, mesh));
write(`${MODEL}_anims/run.dmx`, animDmx(SOURCE, bones, "train", "run", frames, FPS));
write(`${MODEL}.vmdl`, vmdlText(MODEL, "train", [], clips));
console.log(`${MODEL}: ${CARS} cars, ${mesh.faces.length} faces, run ${(2 * HIDE).toFixed(0)} units in ${(T1 - T0).toFixed(1)} s, every ${LOOP} s`);
// the train joins the preview scene of the local viewer, standing over the court (optional third argument)
if (process.argv[3]) {
  const dir = process.argv[3], sceneFile = path.join(dir, "scene.json"), binFile = path.join(dir, "scene.bin"), manifest = JSON.parse(fs.readFileSync(sceneFile, "utf8")), data = [];
  for (const f of mesh.faces) for (const k of [0, 1, 2, 0, 2, 3]) data.push(...mesh.positions[f[k]], ...mesh.normals[f[k]], ...mesh.uvs[f[k]]);
  const bin = fs.readFileSync(binFile);
  manifest.materials = manifest.materials.filter((m) => m.name !== "train");
  manifest.materials.push({ name: "train", offset: bin.length, vertices: data.length / 8, twoSided: true });
  fs.writeFileSync(binFile, Buffer.concat([bin, Buffer.from(new Float32Array(data).buffer)])); fs.writeFileSync(sceneFile, JSON.stringify(manifest));
}
