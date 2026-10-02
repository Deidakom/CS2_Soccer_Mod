#!/usr/bin/env node
// SoccerMod street arena: the canopy of festival flags over the court and the kites over the roofs
// (owner 2026-10-02: the court's eye-catcher instead of the net that closed it on top).
// One model, one looping clip "sway": every string of flags hangs on its own bone and swings a little
// in the wind (the ends stay where they are tied), every kite circles on its own bone and pulls its
// line along. The map places it as a prop_dynamic with that clip - no plugin needed, nothing of it
// is solid: the ball flies through the flags.
//   models/soccermod_street/canopy.vmdl   (material materials/soccermod_street/canopy.vmat: a sheet of
//   plain colours for the flags and four kites, generate-street-textures.mjs)
//
// usage: node tools/street/generate-street-canopy.mjs <content addon dir> [<preview dir>]
import fs from "node:fs";
import path from "node:path";
import { meshDmx, animDmx, vmdlText } from "../atmo/dmx-lib.mjs";
import { Z, LAMPS, EL, STRINGS, CANOPY } from "./layout.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-street-canopy.mjs <content addon dir> [<preview dir>]"); process.exit(1); }
const SOURCE = "tools/street/generate-street-canopy.mjs", MODEL = "models/soccermod_street/canopy", MAT = "materials/soccermod_street/canopy.vmat";
const FPS = 30, LOOP = 12, FLAG_COLOURS = 7, STRING_CELL = 7;
const XA = LAMPS.x, XB = EL.cx - EL.width / 2 - 5, HA = STRINGS.h0, HB = STRINGS.h1;

const bones = [{ name: "root", pos: [0, 0, 0] }];
const mesh = { name: "canopy", material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
// one corner: position, uv, and how much of it follows `bone` (the rest stays with the root)
const vert = (p, uv, bone, w, n = [0, 0, 1]) => { mesh.positions.push(p); mesh.normals.push(n); mesh.uvs.push(uv); mesh.weights.push([w, 1 - w]); mesh.indices.push([bone, 0]); return mesh.positions.length - 1; };
const quad = (corners) => { const ids = corners.map(([p, uv, bone, w, n]) => vert(p, uv, bone, w, n)); mesh.faces.push([ids[0], ids[3], ids[2], ids[1]]); };
// uv of a plain colour cell (the middle of it) and of a kite picture
const cell = (k) => [(k + 0.5) / 8, 0.25], kiteUv = (k) => [k / 4, 0.5, (k + 1) / 4, 1];

// ---- the strings of flags ---------------------------------------------------------------------------
let flags = 0;
CANOPY.strings.forEach(([ya, yb], s) => {
  const a = [XA, ya, Z(HA)], b = [XB, yb, Z(HB)], len = Math.hypot(b[0] - a[0], b[1] - a[1]), dir = [(b[0] - a[0]) / len, (b[1] - a[1]) / len];
  const at = (t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t - Math.sin(t * Math.PI) * CANOPY.sag];
  const follow = (t) => Math.sin(t * Math.PI) ** 0.8;
  bones.push({ name: `string_${s + 1}`, pos: at(0.5) });
  const bone = bones.length - 1, n = [-dir[1], dir[0], 0], segs = 24;
  // the string itself: two thin crossed strips per piece
  for (let k = 0; k < segs; k++) {
    const t0 = k / segs, t1 = (k + 1) / segs, p = at(t0), q = at(t1), uv = cell(STRING_CELL);
    quad([[[p[0], p[1], p[2] - 0.4], uv, bone, follow(t0), n], [[q[0], q[1], q[2] - 0.4], uv, bone, follow(t1), n], [[q[0], q[1], q[2] + 0.4], uv, bone, follow(t1), n], [[p[0], p[1], p[2] + 0.4], uv, bone, follow(t0), n]]);
    quad([[[p[0] - n[0] * 0.4, p[1] - n[1] * 0.4, p[2]], uv, bone, follow(t0)], [[q[0] - n[0] * 0.4, q[1] - n[1] * 0.4, q[2]], uv, bone, follow(t1)], [[q[0] + n[0] * 0.4, q[1] + n[1] * 0.4, q[2]], uv, bone, follow(t1)], [[p[0] + n[0] * 0.4, p[1] + n[1] * 0.4, p[2]], uv, bone, follow(t0)]]);
  }
  // the flags: pennants hanging from it, their colours in turn; every string starts with another colour
  const count = Math.floor(len / CANOPY.flagEvery);
  for (let k = 1; k < count; k++) {
    const t = k / count, w = follow(t), c = at(t), hw = CANOPY.flagW / 2, uv = cell((k + s * 3) % FLAG_COLOURS), turn = ((k * 7 + s * 5) % 5 - 2) * 0.12;
    const dx = dir[0] * Math.cos(turn) - dir[1] * Math.sin(turn), dy = dir[1] * Math.cos(turn) + dir[0] * Math.sin(turn), nn = [-dy, dx, 0];
    quad([[[c[0] - dx * 1.2, c[1] - dy * 1.2, c[2] - CANOPY.flagH], uv, bone, w, nn], [[c[0] + dx * 1.2, c[1] + dy * 1.2, c[2] - CANOPY.flagH], uv, bone, w, nn], [[c[0] + dx * hw, c[1] + dy * hw, c[2] - 0.3], uv, bone, w, nn], [[c[0] - dx * hw, c[1] - dy * hw, c[2] - 0.3], uv, bone, w, nn]]);
    flags++;
  }
});
const stringBones = bones.length - 1;

// ---- the kites --------------------------------------------------------------------------------------
CANOPY.kites.forEach((kite, k) => {
  const [x, y, h] = kite.at, c = [x, y, Z(h)], from = [kite.from[0], kite.from[1], Z(kite.from[2])];
  bones.push({ name: `kite_${k + 1}`, pos: c });
  const bone = bones.length - 1, [u0, v0, u1, v1] = kiteUv(k % 4), hw = 34, up = 30, down = 58;
  // the kite faces the court's centre and leans back into the wind
  const tl = Math.hypot(x, y) || 1, f = [-x / tl, -y / tl], r = [-f[1], f[0]], lean = 0.45;
  const P = (a, b) => [c[0] + r[0] * a - f[0] * b * lean, c[1] + r[1] * a - f[1] * b * lean, c[2] + b];
  quad([[P(-hw, -down), [u0, v1], bone, 1, [f[0], f[1], 0.4]], [P(hw, -down), [u1, v1], bone, 1, [f[0], f[1], 0.4]], [P(hw, up), [u1, v0], bone, 1, [f[0], f[1], 0.4]], [P(-hw, up), [u0, v0], bone, 1, [f[0], f[1], 0.4]]]);
  // the tail: little bows on a thread below it
  for (let q = 1; q <= 6; q++) { const b = -down - q * 16, sway = Math.sin(q * 1.3 + k) * 7, uv = cell((q + k) % FLAG_COLOURS); quad([[P(sway - 5, b - 4), uv, bone, 1], [P(sway + 5, b - 4), uv, bone, 1], [P(sway + 5, b + 4), uv, bone, 1], [P(sway - 5, b + 4), uv, bone, 1]]); }
  // the line down to the roof it is flown from: the far end stays, the near end follows the kite
  const uv = cell(STRING_CELL), segs = 6, lp = (t) => [from[0] + (c[0] - from[0]) * t, from[1] + (c[1] - from[1]) * t, from[2] + (c[2] - 40 - from[2]) * t - Math.sin(t * Math.PI) * 26];
  for (let q = 0; q < segs; q++) { const t0 = q / segs, t1 = (q + 1) / segs, p = lp(t0), s = lp(t1); quad([[[p[0] - r[0] * 0.7, p[1] - r[1] * 0.7, p[2]], uv, bone, t0 ** 1.5], [[s[0] - r[0] * 0.7, s[1] - r[1] * 0.7, s[2]], uv, bone, t1 ** 1.5], [[s[0] + r[0] * 0.7, s[1] + r[1] * 0.7, s[2]], uv, bone, t1 ** 1.5], [[p[0] + r[0] * 0.7, p[1] + r[1] * 0.7, p[2]], uv, bone, t0 ** 1.5]]); }
});

// ---- the clip: everything swings in whole turns per loop, so the loop has no seam -----------------------
const frames = [], n = LOOP * FPS, W = 2 * Math.PI / LOOP;
for (let f = 0; f <= n; f++) {
  const t = f / FPS, m = new Map();
  for (let b = 1; b <= stringBones; b++) { const ph = b * 1.7; m.set(b, [Math.sin(W * t * 2 + ph) * 2.2, Math.sin(W * t + ph) * 9 + Math.sin(W * t * 3 + ph * 2) * 2.5, Math.sin(W * t * 2 + ph * 0.6) * 3.2]); }
  for (let b = stringBones + 1; b < bones.length; b++) { const ph = b * 2.3; m.set(b, [Math.sin(W * t + ph) * 46, Math.sin(W * t * 2 + ph) * 22, Math.sin(W * t + ph + 1.2) * 30 + Math.sin(W * t * 3 + ph) * 8]); }
  frames.push(m);
}
const clips = [{ name: "sway", looping: true, frames }];
const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${MODEL}.dmx`, meshDmx(SOURCE, bones, mesh));
write(`${MODEL}_anims/sway.dmx`, animDmx(SOURCE, bones, "canopy", "sway", frames, FPS));
write(`${MODEL}.vmdl`, vmdlText(MODEL, "canopy", [], clips));
console.log(`${MODEL}: ${CANOPY.strings.length} strings with ${flags} flags, ${CANOPY.kites.length} kites, ${mesh.faces.length} faces, ${bones.length} bones, loop ${LOOP} s`);
// the canopy joins the preview scene of the local viewer (optional second argument)
if (process.argv[3]) {
  const dir = process.argv[3], sceneFile = path.join(dir, "scene.json"), binFile = path.join(dir, "scene.bin"), manifest = JSON.parse(fs.readFileSync(sceneFile, "utf8")), data = [];
  for (const f of mesh.faces) for (const k of [0, 1, 2, 0, 2, 3]) data.push(...mesh.positions[f[k]], ...mesh.normals[f[k]], ...mesh.uvs[f[k]]);
  const bin = fs.readFileSync(binFile);
  manifest.materials = manifest.materials.filter((m) => m.name !== "canopy");
  manifest.materials.push({ name: "canopy", offset: bin.length, vertices: data.length / 8, twoSided: true });
  fs.writeFileSync(binFile, Buffer.concat([bin, Buffer.from(new Float32Array(data).buffer)])); fs.writeFileSync(sceneFile, JSON.stringify(manifest));
}
