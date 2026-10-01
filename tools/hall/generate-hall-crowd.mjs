#!/usr/bin/env node
// SoccerMod indoor hall: the fans. Same idea as the stadium's crowd (tools/arena/generate-arena-
// crowd.mjs): cut-out cards from the fan atlas (tools/arena/lib/fans.mjs), one model per stand,
// bone columns along the stand, clips idle / cheer / wave / goal_red / goal_blue. The map places
// them as prop_dynamic with the idle clip; the plugin can play the other clips by name.
//   models/soccermod_hall/crowd_west.vmdl  crowd_east.vmdl  crowd_end_red.vmdl  crowd_end_blue.vmdl
// Rows come from the layout file (generate-hall.mjs --layout): seats, the concourse rails, the
// terraces behind the goals, the two team benches and their coaches.
//
// usage: node tools/hall/generate-hall-crowd.mjs <content addon dir> <hall-layout.json> [preview dir]
import fs from "node:fs";
import path from "node:path";
import { meshDmx, animDmx, vmdlText } from "../atmo/dmx-lib.mjs";
import { fanAtlas, FAN_ATLAS } from "../arena/lib/fans.mjs";

const [out, layoutFile, previewDir] = process.argv.slice(2);
if (!out || !layoutFile) { console.error("usage: generate-hall-crowd.mjs <addon content dir> <hall-layout.json> [preview dir]"); process.exit(1); }
const layout = JSON.parse(fs.readFileSync(layoutFile, "utf8")), Z = (h) => layout.floor + h;
const SOURCE = "tools/hall/generate-hall-crowd.mjs";
const DIR = "models/soccermod_hall", MAT = "materials/soccermod_hall/crowd.vmat";
const FPS = 30, A = FAN_ATLAS, COLS = 12, SPACING = 23, FAN_W = 31, FAN_H = 62, EMPTY = 0.04, TURN = 0.35;
const HIP = (6 + 104) / A.cellH, SET_NAMES = ["col", "red", "blue"];
const ADULT = { 0: [1, 4, 5, 7, 8, 10], 1: [1, 3, 5, 6, 10, 12] };   // team rows: grown-ups in a clear team shirt

let seed = 20261003;
const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);

function build(name) {
  const rows = layout.crowd.filter((r) => r.name === name), group = name === "end_red" ? 0 : name === "end_blue" ? 1 : 2;
  const mesh = { name: `crowd_${name}`, material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
  // the stand's extent along its length (y for the long sides, x for the ends) decides the bone column
  const along = (p) => (group === 2 ? p[1] : p[0]), ends = rows.flatMap((r) => [along(r.a), along(r.b)]), lo = Math.min(...ends), hi = Math.max(...ends);
  const sets = group === 2 ? 3 : 1, bones = [{ name: "root", pos: [0, 0, 0] }];
  const centre = rows.reduce((s, r) => [s[0] + (r.a[0] + r.b[0]) / 2 / rows.length, s[1] + (r.a[1] + r.b[1]) / 2 / rows.length], [0, 0]);
  for (let set = 0; set < sets; set++) for (let c = 0; c < COLS; c++) {
    const t = lo + (hi - lo) * (c + 0.5) / COLS;
    bones.push({ name: `${SET_NAMES[set]}${c}`, pos: group === 2 ? [centre[0], t, Z(120)] : [t, centre[1], Z(120)] });
  }
  const columnOf = (p) => Math.min(COLS - 1, Math.max(0, Math.floor((along(p) - lo) / (hi - lo) * COLS)));
  let fans = 0;
  const card = (p, z, w, h, col, row, look, turn, bone) => {
    const ct = Math.cos(turn), st = Math.sin(turn), nx = -look[0], ny = -look[1], rx = ny * ct - nx * st, ry = -nx * ct - ny * st;
    const u0 = col / A.cols, u1 = (col + 1) / A.cols, v0 = row * A.cellH / A.size, v1 = (row + 1) * A.cellH / A.size, base = mesh.positions.length;
    for (const [dr, dz, u, v] of [[-w / 2, 0, u0, v1], [w / 2, 0, u1, v1], [w / 2, h, u1, v0], [-w / 2, h, u0, v0]]) {
      mesh.positions.push([p[0] + rx * dr, p[1] + ry * dr, z + dz]); mesh.normals.push([look[0], look[1], 0]); mesh.uvs.push([u, v]);
      mesh.weights.push([1, 0]); mesh.indices.push([bone, 0]);
    }
    mesh.faces.push([base, base + 3, base + 2, base + 1]);
    fans++;
  };
  for (const r of rows) {
    const len = Math.hypot(r.b[0] - r.a[0], r.b[1] - r.a[1]), g = r.group;
    const at = (t) => [r.a[0] + (r.b[0] - r.a[0]) * t, r.a[1] + (r.b[1] - r.a[1]) * t];
    if (r.coach) { card(r.a, Z(r.h), 36, 72, g === 0 ? 14 : 15, g * 2, r.look, 0, columnOf(r.a) + 1); continue; }
    if (r.bench) {
      const n = 5, cols = ADULT[g];
      for (let k = 0; k < n; k++) { const h = 64 * (0.95 + rnd() * 0.1); card(at((k + 0.5) / n), Z(r.seatH) - HIP * h, 32, h, cols[k % cols.length], g * 2, r.look, (rnd() - 0.5) * 0.3, columnOf(at((k + 0.5) / n)) + 1); }
      continue;
    }
    const count = Math.floor(len / SPACING), lead = (len - count * SPACING) / 2;
    for (let s = 0; s < count; s++) {
      if (rnd() < (r.sparse ? 1 - r.sparse : EMPTY)) continue;
      const p = at((lead + (s + 0.5) * SPACING + (rnd() - 0.5) * 6) / len), h = FAN_H * (0.92 + rnd() * 0.14), w = FAN_W * (0.92 + rnd() * 0.12);
      const variant = Math.floor(rnd() * A.cols), pose = rnd() < 0.2 ? 1 : 0, row = g * 2 + pose + (g === 2 && rnd() < 0.5 ? 2 : 0);
      const set = g === 2 ? (variant === A.mixedRed ? 1 : variant === A.mixedBlue ? 2 : 0) : 0;
      card(p, Z(r.h) + 1, w, h, variant, row, r.look, (rnd() - 0.5) * 2 * TURN, set * COLS + columnOf(p) + 1);
    }
  }
  return { mesh, bones, fans, group };
}

// group 0 red end, 1 blue end, 2 mixed side. Bone b > 0: column (b - 1) % COLS, colour set (b - 1) / COLS.
function clips(bones, group) {
  const phase = bones.map(() => rnd() * Math.PI * 2);
  const clip = (seconds, fn) => {
    const n = Math.round(seconds * FPS), frames = [];
    for (let f = 0; f <= n; f++) { const t = f / FPS, m = new Map(); for (let b = 1; b < bones.length; b++) { const d = fn(t, (b - 1) % COLS, phase[b], Math.floor((b - 1) / COLS)); if (d) m.set(b, [0, 0, d]); } frames.push(m); }
    return frames;
  };
  const idle = (t, c, ph) => 1.2 * Math.sin(2 * Math.PI * t / 2 + ph);
  const cheer = (t, c, ph) => 11 * Math.max(0, Math.sin(2 * Math.PI * 2 * t + ph));
  const ecstatic = (t, c, ph) => 22 * Math.abs(Math.sin(Math.PI * 2.5 * t + ph));
  const goal = (team) => (t, c, ph, set) => {
    const own = group === team || (group === 2 && set === team + 1), other = group === 1 - team || (group === 2 && set === 2 - team);
    return own ? ecstatic(t, c, ph) : other ? 0.6 * Math.sin(2 * Math.PI * t / 2 + ph) - 0.6 : cheer(t, c, ph);
  };
  return [
    { name: "idle", looping: true, frames: clip(2.0, idle) },
    { name: "cheer", looping: true, frames: clip(1.0, cheer) },
    { name: "wave", looping: false, frames: clip(2.0, (t, c) => { const head = t / 2 * (COLS + 8) - 4; return 24 * Math.exp(-((c - head) ** 2) / 3.5); }) },
    { name: "goal_red", looping: true, frames: clip(2.0, goal(0)) },
    { name: "goal_blue", looping: true, frames: clip(2.0, goal(1)) },
  ];
}

{
  const { color, alpha } = fanAtlas();
  for (const dir of [path.join(out, "materials/soccermod_hall"), previewDir && path.join(previewDir, "tex")].filter(Boolean)) {
    fs.mkdirSync(dir, { recursive: true });
    color.png(path.join(dir, "crowd_color.png"), 3); alpha.png(path.join(dir, "crowd_trans.png"), 1);
  }
  fs.writeFileSync(path.join(out, "materials/soccermod_hall/crowd.vmat"), `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"F_ALPHA_TEST"	"1"
	"F_RENDER_BACKFACES"	"1"
	"F_DO_NOT_CAST_SHADOWS"	"1"
	"g_flAlphaTestReference"	"0.450"
	"g_flMetalness"	"0.000"
	"TextureColor"	"materials/soccermod_hall/crowd_color.png"
	"TextureTranslucency"	"materials/soccermod_hall/crowd_trans.png"
	"TextureRoughness"	"[0.850000 0.850000 0.850000 0.000000]"
}
`);
}
const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
let total = 0; const previewData = [];
for (const section of ["west", "east", "end_red", "end_blue"]) {
  const { mesh, bones, fans, group } = build(section), name = `crowd_${section}`, model = `${DIR}/${name}`, cl = clips(bones, group);
  write(`${model}.dmx`, meshDmx(SOURCE, bones, mesh));
  for (const c of cl) write(`${model}_anims/${c.name}.dmx`, animDmx(SOURCE, bones, name, c.name, c.frames, FPS));
  write(`${model}.vmdl`, vmdlText(model, name, [], cl));
  total += fans;
  console.log(`${model}: ${fans} fans`);
  for (const f of mesh.faces) for (const k of [0, 1, 2, 0, 2, 3]) previewData.push(...mesh.positions[f[k]], ...mesh.normals[f[k]], ...mesh.uvs[f[k]]);
}
console.log(`total ${total} fans`);
// the fans join the preview scene as one more material
if (previewDir) {
  const sceneFile = path.join(previewDir, "scene.json"), binFile = path.join(previewDir, "scene.bin");
  const manifest = JSON.parse(fs.readFileSync(sceneFile, "utf8")), bin = fs.readFileSync(binFile), buf = Buffer.from(new Float32Array(previewData).buffer);
  manifest.materials = manifest.materials.filter((m) => m.name !== "crowd");
  manifest.materials.push({ name: "crowd", offset: bin.length, vertices: previewData.length / 8, twoSided: true });
  fs.writeFileSync(binFile, Buffer.concat([bin, buf])); fs.writeFileSync(sceneFile, JSON.stringify(manifest));
  const matFile = path.join(previewDir, "materials.json"), mats = fs.existsSync(matFile) ? JSON.parse(fs.readFileSync(matFile, "utf8")) : {};
  mats.crowd = { map: "crowd_color.png", alpha: "crowd_trans.png", alphaTest: 0.45, roughness: 0.85, metalness: 0, twoSided: true, noShadow: true };
  fs.writeFileSync(matFile, JSON.stringify(mats, null, 1));
}
