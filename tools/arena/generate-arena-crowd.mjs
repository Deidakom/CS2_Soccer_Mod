#!/usr/bin/env node
// SoccerMod Arena: the stadium crowd for the new stands. Same idea, atlas, sections, bones
// and clips as tools/atmo/generate-crowd-stadium.mjs (v8), so the plugin only needs another
// model folder: models/soccermod/atmo/crowd_arena/<section>.vmdl
//   sections: end_red / end_blue / side_east / side_west, each _lower and _upper
//   24 bone columns per section (the sides: three colour sets), clips idle / cheer / wave /
//   goal_red / goal_blue; rows run counter-clockwise so the Mexican wave travels one way.
// Fans stand on the treads of layout.mjs, in front of their seats; none in the aisles, the
// vomitories or on the tunnel heads.
//
// usage: node tools/arena/generate-arena-crowd.mjs <soccermod_atmo content dir>
import fs from "node:fs";
import path from "node:path";
import { meshDmx, animDmx, vmdlText } from "../atmo/dmx-lib.mjs";
import { RING, P, station, lowerRow, upperRow, LOWER, UPPER } from "./layout.mjs";
import { fanAtlas, FAN_ATLAS } from "./lib/fans.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-arena-crowd.mjs <addon content dir>"); process.exit(1); }
const SOURCE = "tools/arena/generate-arena-crowd.mjs";
const DIR = "models/soccermod/atmo/crowd_arena";
const MAT = "materials/soccermod_arena/crowd.vmat";            // own atlas (lib/fans.mjs): 16 x 8 shaded figures
const FPS = 30, AH = FAN_ATLAS.size, CH = FAN_ATLAS.cellH, VARIANTS = FAN_ATLAS.cols, COLS = 24;
const SPACING = 23, STAND_IN = 13, FAN_W = 30, FAN_H = 60, MARGIN = 8, EMPTY = 0.015, TURN = 0.4;
const MIXED_RED = FAN_ATLAS.mixedRed, MIXED_BLUE = FAN_ATLAS.mixedBlue, SET_NAMES = ["col", "red", "blue"];
const { N, kinds, blocks } = RING;
const mod = (i) => ((i % N) + N) % N;

let seed = 20261001;
const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
const pickVariant = (group, r) => Math.floor(r * VARIANTS);

// which section a ring segment belongs to (by the direction it looks from)
const sectionOf = (i) => { const a = station(i), b = station(i + 1), nx = a.nx + b.nx, ny = a.ny + b.ny; return ny * ny > nx * nx ? (ny > 0 ? "end_red" : "end_blue") : (nx > 0 ? "side_east" : "side_west"); };
// the segments of a section in counter-clockwise order, numbered without a wrap
function sectionSegments(name) {
  let start = 0; while (!(sectionOf(start) === name && sectionOf(mod(start - 1)) !== name)) start++;
  const list = []; for (let i = start; sectionOf(mod(i)) === name && list.length < N; i++) list.push(i);
  return list;
}

function buildSection(name, tierName, group) {
  const tier = tierName === "lower" ? { rows: LOWER.rows, row: lowerRow } : { rows: UPPER.rows, row: upperRow };
  const segs = sectionSegments(name), first = segs[0], last = segs.at(-1) + 1;
  const mesh = { name: `${name}_${tierName}`, material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
  const sets = group === 2 ? 3 : 1, z0 = tier.row(0).z;
  const bones = [{ name: "root", pos: [0, 0, 0] }];
  // columns by position along the front row of the tier
  const d0 = tier.row(0).d0 + STAND_IN, len0 = [0];
  for (const i of segs) { const a = P(i, d0, 0), b = P(i + 1, d0, 0); len0.push(len0.at(-1) + Math.hypot(b[0] - a[0], b[1] - a[1])); }
  const total0 = len0.at(-1);
  const columnOf = (f) => { const k = Math.min(segs.length - 1, Math.max(0, Math.floor(f) - first)), t = Math.min(1, Math.max(0, f - first - k)); return Math.min(COLS - 1, Math.floor((len0[k] + (len0[k + 1] - len0[k]) * t) / total0 * COLS)); };
  for (let set = 0; set < sets; set++) for (let c = 0; c < COLS; c++) {
    const want = (c + 0.5) / COLS * total0; let k = 0; while (k < segs.length - 1 && len0[k + 1] < want) k++;
    const p = P(first + k + (want - len0[k]) / (len0[k + 1] - len0[k]), d0, z0 + 100);
    bones.push({ name: set ? `${SET_NAMES[set]}${c}` : `col${c}`, pos: p });
  }
  let fans = 0;
  const inSection = new Set(segs.map(mod));
  for (const block of blocks) {
    // the block's segments inside this section, as runs without a wrap
    const runs = [];
    for (const i of block) { if (!inSection.has(i)) continue; const lastRun = runs.at(-1); if (lastRun && mod(lastRun.at(-1) + 1) === i) lastRun.push(lastRun.at(-1) + 1); else runs.push([i]); }
    for (let k = 0; k < tier.rows; k++) {
      const r = tier.row(k), d = r.d0 + STAND_IN, z = r.z + 1;
      for (const fullRun of runs) {
        const run = fullRun.filter((i) => !(tierName === "lower" && k < 3 && kinds[mod(i)] === "tunnel"));
        if (!run.length) continue;
        const lens = run.map((i) => { const a = P(i, d, 0), b = P(i + 1, d, 0); return Math.hypot(b[0] - a[0], b[1] - a[1]); });
        const len = lens.reduce((s, v) => s + v, 0), count = Math.floor((len - 2 * MARGIN) / SPACING);
        const at = (x) => { let acc = 0; for (let j = 0; j < run.length; j++) { if (x <= acc + lens[j] + 1e-9) return run[j] + Math.min(1, Math.max(0, (x - acc) / lens[j])); acc += lens[j]; } return run.at(-1) + 1; };
        const lead = (len - count * SPACING) / 2;
        for (let s = 0; s < count; s++) {
          if (rnd() < EMPTY) continue;                                           // a few empty seats
          const h = FAN_H * (0.92 + rnd() * 0.14), w = FAN_W * (0.92 + rnd() * 0.12), j = (rnd() - 0.5) * 6;
          let f = at(lead + (s + 0.5) * SPACING + j);
          // unwrap towards the section's own numbering (side_east runs through station 0)
          while (f < first) f += N; while (f >= last + 1e-9) f -= N;
          const i0 = Math.floor(f), a = station(i0), b = station(i0 + 1), t = f - i0;
          let nx = a.nx + (b.nx - a.nx) * t, ny = a.ny + (b.ny - a.ny) * t; const nl = Math.hypot(nx, ny); nx /= nl; ny /= nl;
          // not all in one line: every fan is turned a little
          const turn = (rnd() - 0.5) * 2 * TURN, ct = Math.cos(turn), st = Math.sin(turn);
          const [cx, cy] = P(f, d, 0), rx = ny * ct - nx * st, ry = -nx * ct - ny * st;
          const variant = pickVariant(group, rnd()), pose = rnd() < 0.2 ? 1 : 0;
          // the neutral sides have two rows of figures per pose (32 different people)
          const row = group * 2 + pose + (group === 2 && rnd() < 0.5 ? 2 : 0);
          const u0 = variant / VARIANTS, u1 = (variant + 1) / VARIANTS, v0 = row * CH / AH, v1 = (row + 1) * CH / AH;
          const set = group === 2 ? (variant === MIXED_RED ? 1 : variant === MIXED_BLUE ? 2 : 0) : 0;
          const bone = set * COLS + columnOf(f) + 1, base = mesh.positions.length;
          for (const [dr, dz, u, v] of [[-w / 2, 0, u0, v1], [w / 2, 0, u1, v1], [w / 2, h, u1, v0], [-w / 2, h, u0, v0]]) {
            mesh.positions.push([cx + rx * dr, cy + ry * dr, z + dz]); mesh.normals.push([-nx, -ny, 0]); mesh.uvs.push([u, v]);
            mesh.weights.push([1, 0]); mesh.indices.push([bone, 0]);
          }
          mesh.faces.push([base, base + 3, base + 2, base + 1]);
          fans++;
        }
      }
    }
  }
  return { mesh, bones, fans };
}

// group 0 red end, 1 blue end, 2 mixed side. Bone b > 0: column (b - 1) % COLS, colour set (b - 1) / COLS.
function clips(bones, group) {
  const phase = bones.map(() => rnd() * Math.PI * 2);
  const clip = (seconds, fn) => {
    const n = Math.round(seconds * FPS), frames = [];
    for (let f = 0; f <= n; f++) {
      const t = f / FPS, m = new Map();
      for (let b = 1; b < bones.length; b++) { const d = fn(t, (b - 1) % COLS, phase[b], Math.floor((b - 1) / COLS)); if (d) m.set(b, [0, 0, d]); }
      frames.push(m);
    }
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
  const { color, alpha } = fanAtlas(), dir = path.join(out, "materials/soccermod_arena");
  fs.mkdirSync(dir, { recursive: true });
  color.png(path.join(dir, "crowd_color.png"), 3); alpha.png(path.join(dir, "crowd_trans.png"), 1);
  fs.writeFileSync(path.join(dir, "crowd.vmat"), `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"F_ALPHA_TEST"	"1"
	"F_RENDER_BACKFACES"	"1"
	"F_DO_NOT_CAST_SHADOWS"	"1"
	"g_flAlphaTestReference"	"0.450"
	"g_flMetalness"	"0.000"
	"TextureColor"	"materials/soccermod_arena/crowd_color.png"
	"TextureTranslucency"	"materials/soccermod_arena/crowd_trans.png"
	"TextureRoughness"	"[0.850000 0.850000 0.850000 0.000000]"
}
`);
}
const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
let total = 0;
for (const [section, group] of [["end_red", 0], ["end_blue", 1], ["side_east", 2], ["side_west", 2]]) for (const tier of ["lower", "upper"]) {
  const { mesh, bones, fans } = buildSection(section, tier, group), name = `${section}_${tier}`, model = `${DIR}/${name}`, cl = clips(bones, group);
  write(`${model}.dmx`, meshDmx(SOURCE, bones, mesh));
  for (const c of cl) write(`${model}_anims/${c.name}.dmx`, animDmx(SOURCE, bones, name, c.name, c.frames, FPS));
  write(`${model}.vmdl`, vmdlText(model, name, [], cl));
  total += fans;
  console.log(`${model}: ${fans} fans`);
}
console.log(`total ${total} fans`);
