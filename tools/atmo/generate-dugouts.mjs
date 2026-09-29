#!/usr/bin/env node
// "Arena Vision" dugouts (2026-09-29 owner: "on these benches I want players sitting and
// waiting, and a coach standing on either side watching the game"). Low-poly 3D figures
// (boxes, flat palette colours, lit by the map) - the dugouts are right next to the pitch, so
// flat fan cards would look wrong up close.
//
// v8 dugouts (measured from the map meshes, /tmp/v8tris3.txt): two glass shelters on the -x
// side, x -1580 (back wall) .. -1500 (front), y 212..470 (north) and -473..-215 (south), bench
// seat top z -13, floor z -32 (= pitch plane).
//
//   models/soccermod/atmo/dugout_subs_red.vmdl   six seated substitutes, north dugout (map coords)
//   models/soccermod/atmo/dugout_subs_blue.vmdl  the same in the south dugout
//   models/soccermod/atmo/coach_red.vmdl         standing coach, origin at the feet, facing +x
//   models/soccermod/atmo/coach_blue.vmdl
// Clips: idle (breathing) and celebrate (subs jump with arms up, the coach pumps his fist).
//
// usage: node tools/atmo/generate-dugouts.mjs <addon content dir>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { meshDmx, animDmx, vmdlText } from "./dmx-lib.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-dugouts.mjs <addon content dir>"); process.exit(1); }
const SOURCE = "tools/atmo/generate-dugouts.mjs";
const DIR = "models/soccermod/atmo";
const MAT = "materials/soccermod/atmo/dugout_figures.vmat";
const FPS = 30;
let seed = 20260929;
const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);

// ---- palette texture: 16 x 16 swatches of 4 x 4 px -----------------------------------------
const palette = [];
const swatch = (rgb) => { const k = rgb.join(","); let i = palette.findIndex((p) => p.join(",") === k); if (i < 0) { i = palette.length; palette.push(rgb); } return i; };
const uvOf = (i) => [((i % 16) * 4 + 2) / 64, (Math.floor(i / 16) * 4 + 2) / 64];

const SKINS = [[241, 199, 166], [224, 172, 133], [198, 138, 95], [141, 90, 59], [94, 58, 38]];
const HAIRS = [[29, 26, 24], [59, 42, 30], [107, 74, 43], [176, 138, 74], [20, 20, 22]];
const TEAMS = {
  red: { shirt: [196, 34, 30], trim: [238, 238, 234], shorts: [30, 30, 34], socks: [196, 34, 30], jacket: [120, 20, 18] },
  blue: { shirt: [34, 78, 200], trim: [238, 238, 234], shorts: [30, 30, 34], socks: [34, 78, 200], jacket: [20, 44, 120] },
};
const BLACK = [24, 24, 26], DARK = [44, 46, 52], WHITE = [238, 238, 234];

// ---- mesh building ---------------------------------------------------------------------------
const newMesh = (name) => ({ name, material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] });
// axis-aligned box, local offset (ox, oy, oz), one colour, skinned to one bone
function box(m, bone, [x0, x1], [y0, y1], [z0, z1], rgb, o = [0, 0, 0]) {
  const uv = uvOf(swatch(rgb));
  const c = [(x0 + x1) / 2 + o[0], (y0 + y1) / 2 + o[1], (z0 + z1) / 2 + o[2]], h = [(x1 - x0) / 2, (y1 - y0) / 2, (z1 - z0) / 2];
  // n = axis a, sign s; u x v = n; DMX front faces are clockwise seen from outside
  for (const [a, s] of [[0, 1], [0, -1], [1, 1], [1, -1], [2, 1], [2, -1]]) {
    let u = (a + 1) % 3, v = (a + 2) % 3;
    if (s < 0) [u, v] = [v, u];
    const n = [0, 0, 0]; n[a] = s;
    const P = (su, sv) => { const p = [...c]; p[a] += s * h[a]; p[u] += su * h[u]; p[v] += sv * h[v]; return p; };
    const base = m.positions.length;
    for (const p of [P(-1, -1), P(-1, 1), P(1, 1), P(1, -1)]) { m.positions.push(p); m.normals.push(n); m.uvs.push(uv); m.weights.push([1, 0]); m.indices.push([bone, 0]); }
    m.faces.push([base, base + 1, base + 2, base + 3]);
  }
}

// Seated substitute, facing +x, origin = hip centre on the seat surface (seat 19 u above the floor).
function seatedSub(m, bodyBone, armBone, at, look) {
  const { skin, hair, kit, hairStyle } = look;
  const B = (bone, x, y, z, rgb) => box(m, bone, x, y, z, rgb, at);
  // legs (on the root: they stay seated)
  for (const s of [-1, 1]) {
    const y = [s * 1 - 3.2, s * 1 + 3.2].map((v) => v + s * 3);
    B(0, [-4, 8], y, [0, 6.5], kit.shorts);                 // shorts over the thigh
    B(0, [8, 16], y, [0.3, 6.2], skin);                     // knee
    B(0, [12.5, 18], y, [-15, 2], kit.socks);               // shin with sock
    B(0, [11.5, 22], y.map((v, i) => v + (i ? 0.4 : -0.4)), [-19, -15], BLACK);   // boot
  }
  // body bone: torso, neck, head, hair
  B(bodyBone, [-6, 4], [-8, 8], [5, 28], kit.shirt);
  B(bodyBone, [-6.2, 4.2], [-8.2, 8.2], [25.5, 28.2], kit.trim);   // collar band
  B(bodyBone, [-2, 2], [-2.5, 2.5], [28, 31], skin);
  B(bodyBone, [-4.5, 4.5], [-4, 4], [31, 41], skin);
  B(bodyBone, [4.5, 4.8], [-2.6, -0.8], [36, 37.5], BLACK);         // eyes
  B(bodyBone, [4.5, 4.8], [0.8, 2.6], [36, 37.5], BLACK);
  if (hairStyle === 0) { B(bodyBone, [-5, 4.8], [-4.4, 4.4], [39.5, 42.5], hair); B(bodyBone, [-5, -4], [-4.4, 4.4], [33, 42], hair); }
  else if (hairStyle === 1) { B(bodyBone, [-5, 3], [-4.4, 4.4], [40, 41.5], hair); }
  else { B(bodyBone, [-5.5, 5.5], [-4.6, 4.6], [39, 43], hair); B(bodyBone, [4.5, 9], [-4.6, 4.6], [39, 40], hair); }   // cap with peak
  // arm bone: arms hang next to the torso (sleeve + forearm + hand); celebrate lifts them up
  for (const s of [-1, 1]) {
    const y = s > 0 ? [8.2, 13] : [-13, -8.2];
    B(armBone, [-3, 2], y, [18, 28], kit.shirt);
    B(armBone, [-2.6, 1.6], y.map((v) => v * 0.98), [7, 18], skin);
    B(armBone, [-2.8, 1.8], y.map((v) => v * 0.98), [4, 7], skin);
  }
}

// Standing coach, facing +x, origin at the feet (72 u tall).
function coach(m, bodyBone, armBone, kit, look) {
  const { skin, hair } = look;
  const B = (bone, x, y, z, rgb) => box(m, bone, x, y, z, rgb);
  for (const s of [-1, 1]) {
    const y = s > 0 ? [0.8, 7.5] : [-7.5, -0.8];
    B(bodyBone, [-3.2, 3.2], y, [3, 34], DARK);                   // trousers
    B(bodyBone, [-4, 7], y.map((v, i) => v + (i ? 0.3 : -0.3)), [0, 3.2], BLACK);   // shoes
  }
  B(bodyBone, [-5, 5], [-9, 9], [33, 60], kit.jacket);            // jacket
  B(bodyBone, [5, 5.3], [-0.6, 0.6], [36, 59], kit.trim);         // zip
  B(bodyBone, [-5.2, 5.2], [-9.2, 9.2], [57.5, 60.5], kit.shirt); // collar
  B(bodyBone, [-2, 2], [-2.5, 2.5], [60, 63], skin);
  B(bodyBone, [-4.5, 4.5], [-4, 4], [63, 73], skin);
  B(bodyBone, [4.5, 4.8], [-2.6, -0.8], [68, 69.5], BLACK);
  B(bodyBone, [4.5, 4.8], [0.8, 2.6], [68, 69.5], BLACK);
  B(bodyBone, [-5, 4.8], [-4.4, 4.4], [71.5, 74], hair);
  B(bodyBone, [-5, -4], [-4.4, 4.4], [65, 74], hair);
  // left arm: hand on the hip; right arm on its own bone (fist pump)
  B(bodyBone, [-3, 2], [-13, -9], [45, 59], kit.jacket);
  B(bodyBone, [-1, 4], [-13, -9], [38, 45], kit.jacket);
  B(bodyBone, [1, 5], [-11.5, -8.5], [35.5, 39], skin);
  B(armBone, [-3, 2], [9, 13], [36, 59], kit.jacket);
  B(armBone, [-2.6, 1.6], [9.2, 12.8], [32, 36], skin);
}

// ---- clips -------------------------------------------------------------------------------------
function clipFrames(bones, seconds, fn) {
  const n = Math.round(seconds * FPS), frames = [];
  for (let f = 0; f <= n; f++) { const t = f / FPS, mp = new Map(); for (let b = 1; b < bones.length; b++) { const d = fn(t, bones[b]); if (d) mp.set(b, [0, 0, d]); } frames.push(mp); }
  return frames;
}

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
function emit(model, name, bones, mesh, clips) {
  write(`${model}.dmx`, meshDmx(SOURCE, bones, mesh));
  for (const c of clips) write(`${model}_anims/${c.name}.dmx`, animDmx(SOURCE, bones, name, c.name, c.frames, FPS));
  write(`${model}.vmdl`, vmdlText(model, name, [], clips));
}

// subs: map coordinates. Bench seat centre x -1558 (hips), seat top z -13.
const SEAT_X = -1558, SEAT_Z = -13;
for (const [team, sign] of [["red", 1], ["blue", -1]]) {
  const kit = TEAMS[team], name = `dugout_subs_${team}`, mesh = newMesh(name);
  const bones = [{ name: "root", pos: [0, 0, 0] }];
  const ys = [248, 286, 324, 362, 400, 438];
  ys.forEach((y0, i) => {
    const at = [SEAT_X + (rnd() - 0.5) * 1.5, sign * (y0 + (rnd() - 0.5) * 5), SEAT_Z];
    const body = bones.length, arms = body + 1, phase = rnd() * Math.PI * 2;
    bones.push({ name: `body${i}`, pos: [at[0], at[1], at[2] + 20], phase, kind: "body" }, { name: `arms${i}`, pos: [at[0], at[1], at[2] + 20], phase, kind: "arms" });
    seatedSub(mesh, body, arms, at, { skin: SKINS[Math.floor(rnd() * 5)], hair: HAIRS[Math.floor(rnd() * 5)], kit, hairStyle: Math.floor(rnd() * 3) });
  });
  const clips = [
    { name: "idle", looping: true, frames: clipFrames(bones, 3.0, (t, b) => 0.35 * Math.sin(2 * Math.PI * t / 3 + b.phase)) },
    { name: "celebrate", looping: true, frames: clipFrames(bones, 1.0, (t, b) => {
      const jump = 5 * Math.abs(Math.sin(Math.PI * 2 * t + b.phase));
      return b.kind === "arms" ? jump + 19 + 1.5 * Math.sin(2 * Math.PI * 2 * t + b.phase) : jump;
    }) },
  ];
  emit(`${DIR}/${name}`, name, bones, mesh, clips);
  console.log(`${DIR}/${name}: ${ys.length} subs, ${mesh.positions.length} vertices`);
}

// coaches: local coordinates
for (const [team, look] of [["red", { skin: SKINS[1], hair: HAIRS[4] }], ["blue", { skin: SKINS[3], hair: HAIRS[1] }]]) {
  const name = `coach_${team}`, mesh = newMesh(name);
  const bones = [{ name: "root", pos: [0, 0, 0] }, { name: "body", pos: [0, 0, 36], kind: "body" }, { name: "rarm", pos: [0, 10, 58], kind: "arm" }];
  coach(mesh, 1, 2, TEAMS[team], look);
  const clips = [
    { name: "idle", looping: true, frames: clipFrames(bones, 4.0, (t, b) => (b.kind === "body" ? 0.3 : 0.3) * Math.sin(2 * Math.PI * t / 4)) },
    { name: "celebrate", looping: true, frames: clipFrames(bones, 1.0, (t, b) => {
      const bounce = 2.5 * Math.abs(Math.sin(Math.PI * 2 * t));
      return b.kind === "arm" ? bounce + 24 + 3 * Math.sin(2 * Math.PI * 2 * t) : bounce;
    }) },
  ];
  emit(`${DIR}/${name}`, name, bones, mesh, clips);
  console.log(`${DIR}/${name}: ${mesh.positions.length} vertices`);
}

// ---- palette texture + material ------------------------------------------------------------------
const TW = 64, raw = Buffer.alloc((TW * 3 + 1) * TW);
for (let y = 0; y < TW; y++) for (let x = 0; x < TW; x++) {
  const i = Math.floor(y / 4) * 16 + Math.floor(x / 4), c = palette[i] || [128, 128, 128];
  raw.set(c, y * (TW * 3 + 1) + 1 + x * 3);
}
const crcT = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const v of b) c = crcT[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
const ih = Buffer.alloc(13); ih.writeUInt32BE(TW, 0); ih.writeUInt32BE(TW, 4); ih[8] = 8; ih[9] = 2;
write("materials/soccermod/atmo/dugout_figures_color.png", Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ih), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]));
write(MAT, `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_RENDER_BACKFACES"\t"1"
\t"g_flMetalness"\t"0.000"
\t"TextureColor"\t"materials/soccermod/atmo/dugout_figures_color.png"
\t"TextureRoughness"\t"[0.850000 0.850000 0.850000 0.000000]"
}
`);
if (palette.length > 256) throw new Error(`palette overflow: ${palette.length}`);
console.log(`palette ${palette.length} colours`);
