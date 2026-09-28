#!/usr/bin/env node
// Dynamic goal net (owner 2026-09-28: the net should move in waves like a
// real one when the ball hits it). CS2 runs no client code, so the net is a
// skinned model with baked animations:
//  - our own net mesh in the exact shape of the map's net brushes
//    (soccer_cssl_stadium_v8 func_brush 2:49167:210/215, measured with
//    ValveResourceFormat): roof, slanted back net, two sides;
//  - a lattice of bones per panel; the panel edges are tied to the frame
//    (posts, crossbar, map back struts, goal_frame tubes) and never move;
//  - one short animation per impact spot and strength, baked from a damped
//    membrane simulation: a pocket where the ball hits, ripples running out,
//    settled after ~1.5 s (the last frames are exactly the rest pose).
// The plugin (DynamicNet.cs) hides the map's net brushes (their collision
// stays), spawns this model at both goals and plays the animation of the
// spot nearest to where the ball touches the net.
//
// Frame (like goal_frame): origin = centre of the goal line on the floor,
// +y into the goal, z up. Rope texture from generate-goal-net.mjs, one repeat
// per 16 units (the look the owner picked).
//
// usage: node tools/net/generate-dynamic-net.mjs <csgo_addons/soccermod_menu> [--preview <file.json>]
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

const out = process.argv[2];
if (!out) { console.error("usage: generate-dynamic-net.mjs <addon content dir> [--preview <file.json>]"); process.exit(1); }
const previewFile = process.argv.includes("--preview") ? process.argv[process.argv.indexOf("--preview") + 1] : null;

const MODEL = "models/soccermod/stadium/goal_net_dynamic";
const MAT = "materials/soccermod/stadium/goal_net_dynamic";
const ROPE = "materials/soccer/goalnetting_rope";
const TILE = 16;                                   // units per texture repeat
const HALF = 128, BOT = 4, TOP = 101, TOPD = 47.5, BOTD = 83;

// Panels: bilinear patches P(u, v); NU x NV interior bones, border = fixed.
const panels = [
  { key: "b", P00: [-HALF, BOTD, BOT], P10: [HALF, BOTD, BOT], P01: [-HALF, TOPD, TOP], P11: [HALF, TOPD, TOP], NU: 17, NV: 6, MU: 72, MV: 21 },
  { key: "r", P00: [-HALF, 0, TOP], P10: [HALF, 0, TOP], P01: [-HALF, TOPD, TOP], P11: [HALF, TOPD, TOP], NU: 17, NV: 2, MU: 72, MV: 9 },
  { key: "n", P00: [-HALF, 0, BOT], P10: [-HALF, BOTD, BOT], P01: [-HALF, 0, TOP], P11: [-HALF, TOPD, TOP], NU: 3, NV: 6, MU: 16, MV: 21 },
  { key: "p", P00: [HALF, 0, BOT], P10: [HALF, BOTD, BOT], P01: [HALF, 0, TOP], P11: [HALF, TOPD, TOP], NU: 3, NV: 6, MU: 16, MV: 21 },
];
// Impact spots per panel (u, v) - the plugin picks the nearest by the same
// rule (DynamicNetSpot): back 5 x 3, roof 5 x 1, each side 1 x 2.
const COLS5 = [0.1, 0.3, 0.5, 0.7, 0.9];
const spots = {
  b: { cols: COLS5, rows: [0.18, 0.5, 0.82] },
  r: { cols: COLS5, rows: [0.5] },
  n: { cols: [0.45], rows: [0.3, 0.7] },
  p: { cols: [0.45], rows: [0.3, 0.7] },
};
// Tuned in the browser preview (owner: waves like a real net): a loose net -
// slow waves, little damping, a pocket of ~2/3 ball diameter on a hard shot.
const STRENGTHS = { s: 9, h: 24 };                 // pocket depth (units) of a centre hit
const FPS = 30, DURATION = 2.0, FADE = 0.4;        // seconds
const WAVE_C = 220, DAMPING = 3.5, PULSE = 0.14, SIGMA = 12; // membrane: u/s, 1/s, s, units

// Ball pocket (owner 2026-09-28, "Variante 1"): on the back net the ball
// really sinks in. The plugin (DynamicNet.cs, same constants) lets it pass
// the rest plane, brakes it like a damped spring and pushes it back out; a
// collision shell POCKET.shell units behind the net is the hard stop. These
// "pk_" animations wrap the net around the ball's path instead of a force
// pulse. Spot = where the ball touches the net (9 across x 4 up); strength
// by the ball's speed into the net (s < 650 u/s, m < 1100, h above), each
// animated at its band's top speed so the net is never shallower than the
// ball. Near the frame the net is tighter (edge factor).
// 2026-09-28 owner: 40 % deeper than the first pocket (omega / 1.4, depth and
// shell x 1.4; first try was 20 %) and the
// side nets as well (SIDE_POCKET: the side shells move out while a ball is
// in the goal, NetPocket.cs).
const POCKET = { omega: 28.6, zeta: 0.45, maxOmega: 57, depth: 38, shell: 39, edge: 45, minEdge: 0.3, ballR: 16.36 };
const SIDE_POCKET = { depth: 26, shell: 28 };
const SIDE_COLS = [0.3, 0.55, 0.8], SIDE_ROWS = [0.2, 0.5, 0.8];
const POCKET_COLS = Array.from({ length: 9 }, (_, i) => 0.064 + i * 0.872 / 8);
const POCKET_ROWS = [0.19, 0.42, 0.66, 0.89];
const POCKET_SPEEDS = { s: 650, m: 1100, h: 1900 };
const pocketEdge = (pn, u, v) => Math.min(1, Math.max(POCKET.minEdge, Math.min(Math.min(u, 1 - u) * pn.W(v), v * pn.L, (1 - v) * pn.L) / POCKET.edge));
// ball surface depth past the rest plane t seconds after contact
function pocketDepth(speed, f, t, cap = POCKET.depth) {
  const w = Math.min(POCKET.maxOmega, POCKET.omega / f), z = POCKET.zeta, wd = w * Math.sqrt(1 - z * z);
  if (t <= 0 || t >= Math.PI / wd) return 0;
  return Math.min(cap, (speed / wd) * Math.exp(-z * w * t) * Math.sin(wd * t));
}

const add = (a, b) => a.map((v, i) => v + b[i]);
const sub = (a, b) => a.map((v, i) => v - b[i]);
const mul = (a, s) => a.map((v) => v * s);
const lerp = (a, b, t) => a.map((v, i) => v + (b[i] - v) * t);
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = (a) => mul(a, 1 / Math.hypot(...a));
const P = (pn, u, v) => lerp(lerp(pn.P00, pn.P10, u), lerp(pn.P01, pn.P11, u), v);

// Outward normal (away from the inside of the goal) and physical sizes.
const INSIDE = [0, 30, 50];
for (const pn of panels) {
  let n = norm(cross(sub(pn.P10, pn.P00), sub(pn.P01, pn.P00)));
  if (dot(n, sub(P(pn, 0.5, 0.5), INSIDE)) < 0) n = mul(n, -1);
  pn.n = n;
  pn.W = (v) => Math.hypot(...sub(P(pn, 1, v), P(pn, 0, v)));   // width along u at v
  pn.L = Math.hypot(...sub(P(pn, 0.5, 1), P(pn, 0.5, 0)));       // length along v
}

// ---- bones -------------------------------------------------------------------------
// jointList: 0 = root, then every interior lattice node of every panel.
const bones = [{ name: "root", pos: [0, 0, 0] }];
for (const pn of panels) {
  pn.bone = (i, j) => (i < 1 || j < 1 || i > pn.NU || j > pn.NV ? 0 : pn.boneBase + (j - 1) * pn.NU + (i - 1));
  pn.boneBase = bones.length;
  for (let j = 1; j <= pn.NV; j++) for (let i = 1; i <= pn.NU; i++)
    bones.push({ name: `${pn.key}_${i}_${j}`, pos: P(pn, i / (pn.NU + 1), j / (pn.NV + 1)), pn, i, j });
}

// ---- render mesh (skinned) ------------------------------------------------------------
const positions = [], uvs = [], normals = [], weights = [], indices = [], faces = [];
for (const pn of panels) {
  const base = positions.length;
  for (let b = 0; b <= pn.MV; b++) for (let a = 0; a <= pn.MU; a++) {
    const u = a / pn.MU, v = b / pn.MV, p = P(pn, u, v);
    positions.push(p); normals.push(pn.n);
    uvs.push(pn.key === "b" ? [p[0] / TILE, (1 - v) * pn.L / TILE]
      : pn.key === "r" ? [p[0] / TILE, p[1] / TILE]
      : [p[1] / TILE, (TOP - p[2]) / TILE]);
    // bilinear weights of the 4 lattice nodes around (u, v); border -> root
    const fa0 = u * (pn.NU + 1), fb0 = v * (pn.NV + 1);
    const i0 = Math.min(pn.NU, Math.floor(fa0)), j0 = Math.min(pn.NV, Math.floor(fb0));
    const fa = fa0 - i0, fb = fb0 - j0, w = new Map();
    for (const [di, dj, wt] of [[0, 0, (1 - fa) * (1 - fb)], [1, 0, fa * (1 - fb)], [0, 1, (1 - fa) * fb], [1, 1, fa * fb]]) {
      const k = pn.bone(i0 + di, j0 + dj);
      w.set(k, (w.get(k) || 0) + wt);
    }
    const infl = [...w.entries()].filter(([, wt]) => wt > 1e-4).sort((x, y) => y[1] - x[1]).slice(0, 4);
    const total = infl.reduce((s, [, wt]) => s + wt, 0);
    while (infl.length < 4) infl.push([0, 0]);
    indices.push(infl.map(([k]) => k)); weights.push(infl.map(([, wt]) => wt / total));
  }
  // quads, wound so the face normal is the outward normal
  const pa = P(pn, 0, 0), pb = P(pn, 1 / pn.MU, 0), pc = P(pn, 0, 1 / pn.MV);
  const flip = dot(cross(sub(pb, pa), sub(pc, pa)), pn.n) < 0;
  for (let b = 0; b < pn.MV; b++) for (let a = 0; a < pn.MU; a++) {
    const k = base + b * (pn.MU + 1) + a, q = [k, k + 1, k + pn.MU + 2, k + pn.MU + 1];
    faces.push(flip ? q.reverse() : q);
  }
}

// ---- membrane simulation -------------------------------------------------------------
// Displacement w along the outward normal on a ~4 unit grid over the panel,
// border fixed: w_tt = c^2 lap(w) - damping w_t + pulse(x, t). With drive
// ({ speed, f }) there is no pulse: while the ball is in the net the nodes
// under it are pushed to at least the ball's surface (pocketDepth).
function simulate(pn, hu, hv, drive = null) {
  const SU = Math.max(8, Math.round(pn.W(0.5) / 4)) + 1, SV = Math.max(8, Math.round(pn.L / 4)) + 1;
  const w = new Float64Array(SU * SV), vel = new Float64Array(SU * SV), lap = new Float64Array(SU * SV);
  const dxOf = (j) => pn.W(j / (SV - 1)) / (SU - 1), dy = pn.L / (SV - 1);
  const force = new Float64Array(SU * SV);
  for (let j = 0; j < SV; j++) for (let i = 0; i < SU; i++) {
    const v = j / (SV - 1), u = i / (SU - 1);
    const ex = (u - hu) * pn.W(v), ey = (v - hv) * pn.L;
    force[j * SU + i] = Math.exp(-(ex * ex + ey * ey) / (2 * SIGMA * SIGMA));
  }
  const dt = 1 / 600, steps = Math.round(1 / FPS / dt), frames = Math.round(DURATION * FPS);
  const rec = [];
  const sample = () => rec.push(Float64Array.from(w));
  sample();
  let t = 0;
  for (let f = 1; f <= frames; f++) {
    for (let s = 0; s < steps; s++, t += dt) {
      for (let j = 1; j < SV - 1; j++) {
        const dx = dxOf(j), ix2 = 1 / (dx * dx), iy2 = 1 / (dy * dy);
        for (let i = 1; i < SU - 1; i++) {
          const k = j * SU + i;
          lap[k] = (w[k - 1] - 2 * w[k] + w[k + 1]) * ix2 + (w[k - SU] - 2 * w[k] + w[k + SU]) * iy2;
        }
      }
      const pulse = !drive && t < PULSE ? Math.sin(Math.PI * t / PULSE) : 0;
      for (let j = 1; j < SV - 1; j++) for (let i = 1; i < SU - 1; i++) {
        const k = j * SU + i;
        vel[k] += dt * (WAVE_C * WAVE_C * lap[k] - DAMPING * vel[k] + 1e5 * pulse * force[k]);
      }
      for (let k = 0; k < w.length; k++) w[k] += dt * vel[k];
      const depth = drive ? pocketDepth(drive.speed, drive.f, t + dt, drive.cap) : 0;
      if (depth > 0) {
        const R = POCKET.ballR;
        for (let j = 1; j < SV - 1; j++) for (let i = 1; i < SU - 1; i++) {
          const v = j / (SV - 1), u = i / (SU - 1), ex = (u - hu) * pn.W(v), ey = (v - hv) * pn.L, r2 = ex * ex + ey * ey;
          if (r2 >= R * R) continue;
          const k = j * SU + i, target = depth - (R - Math.sqrt(R * R - r2));
          if (w[k] < target) { vel[k] = Math.max(vel[k], (target - w[k]) / dt); w[k] = target; }
        }
      }
    }
    sample();
  }
  const at = (field, u, v) => { // bilinear sample in (u, v)
    const x = u * (SU - 1), y = v * (SV - 1), i = Math.min(SU - 2, Math.floor(x)), j = Math.min(SV - 2, Math.floor(y)), fx = x - i, fy = y - j;
    return (field[j * SU + i] * (1 - fx) + field[j * SU + i + 1] * fx) * (1 - fy) + (field[(j + 1) * SU + i] * (1 - fx) + field[(j + 1) * SU + i + 1] * fx) * fy;
  };
  return { rec, at };
}

// Centre hit of each panel sets that panel's scale: a centre hit of strength
// S reaches a pocket of S units; hits nearer the edges come out smaller, as
// the fixed edges hold the net there.
for (const pn of panels) {
  const { rec, at } = simulate(pn, 0.5, 0.5);
  pn.peak = Math.max(...rec.map((f) => at(f, 0.5, 0.5)));
}

const anims = []; // { name, frames: [[boneIndex -> [x,y,z]]] }
const previewAnims = {};
for (const pn of panels) {
  const sp = spots[pn.key];
  sp.rows.forEach((hv, row) => sp.cols.forEach((hu, col) => {
    const { rec, at } = simulate(pn, hu, hv);
    for (const [strength, depth] of Object.entries(STRENGTHS)) {
      const scale = depth / pn.peak, name = `hit_${pn.key}_${col}_${row}_${strength}`;
      const frames = rec.map((field, f) => {
        const time = f / FPS, fade = time <= DURATION - FADE ? 1 : Math.max(0, (DURATION - time) / FADE) ** 2 * (3 - 2 * Math.max(0, (DURATION - time) / FADE));
        const off = new Map();
        for (let b = 1; b < bones.length; b++) {
          const bn = bones[b];
          if (bn.pn !== pn) continue;
          const d = at(field, bn.i / (pn.NU + 1), bn.j / (pn.NV + 1)) * scale * fade;
          off.set(b, d);
        }
        return off;
      });
      frames[frames.length - 1] = new Map(); // exactly the rest pose at the end
      anims.push({ name, frames, pn });
      if (strength === "h" && previewFile) previewAnims[name] = frames.map((m) => Object.fromEntries([...m].map(([k, d]) => [k, +d.toFixed(2)])));
    }
  }));
}
// Ball pocket animations on the back net (see POCKET): pk_<col>_<row>_<s|m|h>.
const fadeAt = (time) => time <= DURATION - FADE ? 1 : Math.max(0, (DURATION - time) / FADE) ** 2 * (3 - 2 * Math.max(0, (DURATION - time) / FADE));
{
  const pn = panels.find((p) => p.key === "b");
  POCKET_ROWS.forEach((hv, row) => POCKET_COLS.forEach((hu, col) => {
    const f = pocketEdge(pn, hu, hv);
    for (const [strength, speed] of Object.entries(POCKET_SPEEDS)) {
      const { rec, at } = simulate(pn, hu, hv, { speed, f });
      const frames = rec.map((field, fr) => {
        const off = new Map(), fade = fadeAt(fr / FPS);
        for (let b = 1; b < bones.length; b++) {
          const bn = bones[b];
          if (bn.pn === pn) off.set(b, at(field, bn.i / (pn.NU + 1), bn.j / (pn.NV + 1)) * fade);
        }
        return off;
      });
      frames[frames.length - 1] = new Map();
      anims.push({ name: `pk_${col}_${row}_${strength}`, frames, pn });
    }
  }));
}
// Side-net pockets: pks_<n|p>_<col>_<row>_<s|m|h> (u front -> back, v up).
for (const key of ["n", "p"]) {
  const pn = panels.find((p) => p.key === key);
  SIDE_ROWS.forEach((hv, row) => SIDE_COLS.forEach((hu, col) => {
    const f = pocketEdge(pn, hu, hv);
    for (const [strength, speed] of Object.entries(POCKET_SPEEDS)) {
      const { rec, at } = simulate(pn, hu, hv, { speed, f, cap: SIDE_POCKET.depth });
      const frames = rec.map((field, fr) => {
        const off = new Map(), fade = fadeAt(fr / FPS);
        for (let b = 1; b < bones.length; b++) {
          const bn = bones[b];
          if (bn.pn === pn) off.set(b, at(field, bn.i / (pn.NU + 1), bn.j / (pn.NV + 1)) * fade);
        }
        return off;
      });
      frames[frames.length - 1] = new Map();
      anims.push({ name: `pks_${key}_${col}_${row}_${strength}`, frames, pn });
    }
  }));
}

// ---- DMX (keyvalues2) ------------------------------------------------------------------
const id = () => crypto.randomUUID();
const f4 = (n) => Number(n.toFixed(4)).toString();
const list = (vals, ind) => vals.map((v) => `${ind}"${v}"`).join(",\n");
const xf = (xid, pos) => `"DmeTransform"\n{\n\t"id" "elementid" "${xid}"\n\t"name" "string" "transform"\n\t"position" "vector3" "${pos.map(f4).join(" ")}"\n\t"orientation" "quaternion" "0 0 0 1"\n}`;
const indent = (s, t) => s.split("\n").map((l) => t + l).join("\n");

// Skeleton elements shared by the mesh and the animation files.
function skeleton(withDag) {
  const ids = bones.map(() => ({ joint: id(), xf: id(), base: id() }));
  const dag = withDag ? { id: id(), xf: id(), base: id() } : null;
  const jointElems = bones.map((bn, b) => `"DmeJoint"\n{\n\t"id" "elementid" "${ids[b].joint}"\n\t"name" "string" "${bn.name}"\n${indent(`"transform" ${xf(ids[b].xf, bn.pos)}`, "\t")}\n\t"shape" "element" ""\n\t"visible" "bool" "1"\n\t"children" "element_array"\n\t[\n${b === 0 ? bones.slice(1).map((_, k) => `\t\t"element" "${ids[k + 1].joint}"`).join(",\n") : ""}\n\t]\n}`);
  const jointList = [...ids.map((x) => x.joint), ...(dag ? [dag.id] : [])];
  const baseXfs = [...bones.map((bn, b) => xf(ids[b].base, bn.pos)), ...(dag ? [xf(dag.base, [0, 0, 0])] : [])];
  const model = (modelId, name) => `"DmeModel"\n{\n\t"id" "elementid" "${modelId}"\n\t"name" "string" "${name}"\n${indent(`"transform" ${xf(id(), [0, 0, 0])}`, "\t")}\n\t"shape" "element" ""\n\t"visible" "bool" "1"\n\t"children" "element_array"\n\t[\n\t\t"element" "${ids[0].joint}"${dag ? `,\n\t\t"element" "${dag.id}"` : ""}\n\t]\n\t"jointList" "element_array"\n\t[\n${jointList.map((j) => `\t\t"element" "${j}"`).join(",\n")}\n\t]\n\t"baseStates" "element_array"\n\t[\n\t\t"DmeTransformsList"\n\t\t{\n\t\t\t"id" "elementid" "${id()}"\n\t\t\t"name" "string" "bind"\n\t\t\t"transforms" "element_array"\n\t\t\t[\n${baseXfs.map((x) => indent(x, "\t\t\t\t")).join(",\n")}\n\t\t\t]\n\t\t}\n\t]\n\t"axisSystem" "DmeAxisSystem"\n\t{\n\t\t"id" "elementid" "${id()}"\n\t\t"upAxis" "int" "3"\n\t\t"forwardParity" "int" "1"\n\t\t"coordSys" "int" "0"\n\t}\n}`;
  return { ids, dag, jointElems, model };
}

function meshDmx() {
  const sk = skeleton(true), modelId = id(), vd = id(), seq = positions.map((_, i) => i);
  return `<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
\t"skeleton" "element" "${modelId}"
\t"model" "element" "${modelId}"
\t"exportTags" "DmeExportTags"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "exportTags"
\t\t"source" "string" "tools/net/generate-dynamic-net.mjs"
\t}
}

${sk.model(modelId, "goal_net_dynamic")}

${sk.jointElems.join("\n\n")}

"DmeDag"
{
\t"id" "elementid" "${sk.dag.id}"
\t"name" "string" "goal_net_dynamic"
${indent(`"transform" ${xf(sk.dag.xf, [0, 0, 0])}`, "\t")}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "goal_net_dynamic"
\t\t"bindState" "element" "${vd}"
\t\t"currentState" "element" "${vd}"
\t\t"baseStates" "element_array"
\t\t[
\t\t\t"element" "${vd}"
\t\t]
\t\t"deltaStates" "element_array"
\t\t[
\t\t]
\t\t"faceSets" "element_array"
\t\t[
\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "net"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${list(faces.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${MAT}.vmat"
\t\t\t\t}
\t\t\t}
\t\t]
\t\t"deltaStateWeights" "vector2_array"
\t\t[
\t\t]
\t\t"deltaStateWeightsLagged" "vector2_array"
\t\t[
\t\t]
\t\t"visible" "bool" "1"
\t}
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t]
}

"DmeVertexData"
{
\t"id" "elementid" "${vd}"
\t"name" "string" "bind"
\t"vertexFormat" "string_array"
\t[
\t\t"position$0",
\t\t"texcoord$0",
\t\t"normal$0",
\t\t"tangent$0",
\t\t"blendweights$0",
\t\t"blendindices$0"
\t]
\t"jointCount" "int" "4"
\t"flipVCoordinates" "bool" "0"
\t"position$0" "vector3_array"
\t[
${list(positions.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"position$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"texcoord$0" "vector2_array"
\t[
${list(uvs.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"texcoord$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"normal$0" "vector3_array"
\t[
${list(normals.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"normal$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"tangent$0" "vector4_array"
\t[
${list(positions.map(() => "1 0 0 1"), "\t\t")}
\t]
\t"tangent$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"blendweights$0" "float_array"
\t[
${list(weights.flatMap((w) => w.map(f4)), "\t\t")}
\t]
\t"blendindices$0" "int_array"
\t[
${list(indices.flat(), "\t\t")}
\t]
}
`;
}

// One animation clip: every bone gets a position channel (rest + w * normal
// per frame) and a constant orientation channel.
function animDmx(name, frames) {
  const sk = skeleton(false), modelId = id(), listId = id(), clipId = id();
  const times = frames.map((_, f) => f4(f / FPS));
  const channels = bones.map((bn, b) => {
    const moves = frames.some((off) => Math.abs(off.get(b) || 0) > 1e-4);
    const pos = moves ? frames.map((off) => { const d = off.get(b) || 0; return (bn.pn ? add(bn.pos, mul(bn.pn.n, d)) : bn.pos).map(f4).join(" "); }) : "const";
    const xid = sk.ids[b].xf;
    const chan = (suffix, attr, logType, valType, values, def) => `\t\t"DmeChannel"
\t\t{
\t\t\t"id" "elementid" "${id()}"
\t\t\t"name" "string" "${bn.name}_${suffix}"
\t\t\t"fromElement" "element" "${xid}"
\t\t\t"fromAttribute" "string" "${attr}"
\t\t\t"fromIndex" "int" "0"
\t\t\t"toElement" "element" "${xid}"
\t\t\t"toAttribute" "string" "${attr}"
\t\t\t"toIndex" "int" "0"
\t\t\t"mode" "int" "3"
\t\t\t"log" "${logType}"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "${valType} log"
\t\t\t\t"layers" "element_array"
\t\t\t\t[
\t\t\t\t\t"${logType}Layer"
\t\t\t\t\t{
\t\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t\t"name" "string" "${valType} log"
\t\t\t\t\t\t"times" "time_array"
\t\t\t\t\t\t[
${list(values === "const" ? [times[0]] : times, "\t\t\t\t\t\t\t")}
\t\t\t\t\t\t]
\t\t\t\t\t\t"curvetypes" "int_array"
\t\t\t\t\t\t[
\t\t\t\t\t\t]
\t\t\t\t\t\t"values" "${valType}_array"
\t\t\t\t\t\t[
${list(values === "const" ? [def] : values, "\t\t\t\t\t\t\t")}
\t\t\t\t\t\t]
\t\t\t\t\t}
\t\t\t\t]
\t\t\t\t"curveinfo" "element" ""
\t\t\t\t"usedefaultvalue" "bool" "0"
\t\t\t\t"defaultvalue" "${valType}" "${def}"
\t\t\t}
\t\t}`;
    return [
      chan("p", "position", "DmeVector3Log", "vector3", pos, bn.pos.map(f4).join(" ")),
      chan("o", "orientation", "DmeQuaternionLog", "quaternion", "const", "0 0 0 1"),
    ].join(",\n");
  });
  return `<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
\t"skeleton" "element" "${modelId}"
\t"animationList" "element" "${listId}"
\t"exportTags" "DmeExportTags"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "exportTags"
\t\t"source" "string" "tools/net/generate-dynamic-net.mjs"
\t}
}

${sk.model(modelId, "goal_net_dynamic")}

${sk.jointElems.join("\n\n")}

"DmeAnimationList"
{
\t"id" "elementid" "${listId}"
\t"name" "string" "animationList"
\t"animations" "element_array"
\t[
\t\t"element" "${clipId}"
\t]
}

"DmeChannelsClip"
{
\t"id" "elementid" "${clipId}"
\t"name" "string" "${name}"
\t"timeFrame" "DmeTimeFrame"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "timeFrame"
\t\t"start" "time" "0"
\t\t"duration" "time" "${f4((frames.length - 1) / FPS)}"
\t\t"offset" "time" "0"
\t\t"scale" "float" "1"
\t}
\t"color" "color" "0 0 0 0"
\t"text" "string" ""
\t"mute" "bool" "0"
\t"trackGroups" "element_array"
\t[
\t]
\t"displayScale" "float" "1"
\t"channels" "element_array"
\t[
${channels.join(",\n")}
\t]
\t"frameRate" "int" "${FPS}"
}
`;
}

// ---- model, material ----------------------------------------------------------------------
const animNode = (name, looping) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "AnimFile"
\t\t\t\t\t\tname = "${name}"
\t\t\t\t\t\tsource_filename = "${MODEL}_anims/${name}.dmx"
\t\t\t\t\t\tfade_in_time = ${looping ? "0.0" : "0.08"}
\t\t\t\t\t\tfade_out_time = ${looping ? "0.0" : "0.2"}
\t\t\t\t\t\tlooping = ${looping}
\t\t\t\t\t\tdelta = false
\t\t\t\t\t\tworldSpace = false
\t\t\t\t\t\thidden = false
\t\t\t\t\t},`;
const vmdl = `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "MaterialGroupList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "DefaultMaterialGroup"
\t\t\t\t\t\tremaps = [  ]
\t\t\t\t\t\tuse_global_default = false
\t\t\t\t\t\tglobal_default_material = ""
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "goal_net_dynamic"
\t\t\t\t\t\tfilename = "${MODEL}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "AnimationList"
\t\t\t\tchildren =
\t\t\t\t[
${[animNode("idle", true), ...anims.map((a) => animNode(a.name, false))].join("\n")}
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;
// Same look as the map net material (generate-goal-net.mjs), but the model
// is single-sided sheets, so both sides are drawn; no shadows from the net.
const vmat = `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_TRANSLUCENT"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flMetalness"\t"0.000"
\t"TextureColor"\t"${ROPE}_color.png"
\t"TextureTranslucency"\t"${ROPE}_trans.png"
\t"TextureNormal"\t"${ROPE}_normal.png"
\t"TextureRoughness"\t"${ROPE}_rough.png"
}
`;

// ---- collision shell for the ball pocket -----------------------------------------------
// With the pocket on, the plugin turns the map's net brushes off and spawns
// this instead (drawn with EF_NODRAW): roof and sides where the map had
// them, the back POCKET.shell units behind the visible net as the hard stop,
// all overlapping so neither ball nor player slips through. Convex 2-unit
// slabs, one hull each.
// goal_net_collision (back 28, fixed sides) stays in the package for older plugins.
const COL_MODEL = "models/soccermod/stadium/goal_net_shell";
const colSlabs = {};
let colSideSlab = null; // one side slab at local x -1..1; the plugin places it at x = +-(128 + offset)
{
  const ny = 0.93910, nz = 0.34369, sy = -0.34369, sz = 0.93910; // back normal, slant bottom -> top
  const backAt = (o, z) => { const t = (z - BOT - o * nz) / sz; return [BOTD + o * ny + t * sy, BOT + o * nz + t * sz]; };
  const box = (f) => { const c = []; for (let k = 0; k < 2; k++) for (let j = 0; j < 2; j++) for (let i = 0; i < 2; i++) c.push(f(i, j, k)); return c; };
  colSlabs.back = box((i, j, k) => { const [y, z] = backAt(POCKET.shell - 1 + 2 * k, j ? 112 : -4); return [i ? 132 : -132, y, z]; });
  colSlabs.roof = box((i, j, k) => [i ? 129 : -129, j ? 79 : -1, k ? 102 : 100]);
  const sideYZ = [[-1, -4], [backAt(POCKET.shell - 1, -4)[0] + 1, -4], [-1, 102], [backAt(POCKET.shell - 1, 102)[0] + 1, 102]];
  colSideSlab = box((i, j, k) => [-1 + 2 * i, ...sideYZ[j + 2 * k]]);

}
// corner index = i + 2j + 4k
const BOX_FACES = [[0, 2, 6, 4], [1, 5, 7, 3], [0, 4, 5, 1], [2, 3, 7, 6], [0, 1, 3, 2], [4, 6, 7, 5]];
function staticDmx(name, corners, mtl) {
  const pos = [], nrm = [], uv = [], fcs = [];
  for (const c of corners) for (const face of BOX_FACES) {
    const q = face.map((k) => c[k]), n = norm(cross(sub(q[1], q[0]), sub(q[3], q[0]))), b = pos.length;
    q.forEach((p, k) => { pos.push(p); nrm.push(n); uv.push([k === 1 || k === 2 ? 1 : 0, k >= 2 ? 1 : 0]); });
    fcs.push([b, b + 1, b + 2, b + 3]);
  }
  const modelId = id(), dag = id(), vd = id(), seq = pos.map((_, i) => i);
  return `<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
\t"skeleton" "element" "${modelId}"
\t"model" "element" "${modelId}"
}

"DmeModel"
{
\t"id" "elementid" "${modelId}"
\t"name" "string" "${name}"
${indent(`"transform" ${xf(id(), [0, 0, 0])}`, "\t")}
\t"shape" "element" ""
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t\t"element" "${dag}"
\t]
\t"jointList" "element_array"
\t[
\t\t"element" "${dag}"
\t]
\t"baseStates" "element_array"
\t[
\t\t"DmeTransformsList"
\t\t{
\t\t\t"id" "elementid" "${id()}"
\t\t\t"transforms" "element_array"
\t\t\t[
${indent(xf(id(), [0, 0, 0]), "\t\t\t\t")}
\t\t\t]
\t\t}
\t]
\t"axisSystem" "DmeAxisSystem"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"upAxis" "int" "3"
\t\t"forwardParity" "int" "1"
\t\t"coordSys" "int" "0"
\t}
}

"DmeDag"
{
\t"id" "elementid" "${dag}"
\t"name" "string" "${name}"
${indent(`"transform" ${xf(id(), [0, 0, 0])}`, "\t")}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "${name}"
\t\t"bindState" "element" "${vd}"
\t\t"currentState" "element" "${vd}"
\t\t"baseStates" "element_array"
\t\t[
\t\t\t"element" "${vd}"
\t\t]
\t\t"deltaStates" "element_array"
\t\t[
\t\t]
\t\t"faceSets" "element_array"
\t\t[
\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "slabs"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${list(fcs.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${mtl}.vmat"
\t\t\t\t}
\t\t\t}
\t\t]
\t\t"visible" "bool" "1"
\t}
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t]
}

"DmeVertexData"
{
\t"id" "elementid" "${vd}"
\t"name" "string" "bind"
\t"vertexFormat" "string_array"
\t[
\t\t"position$0",
\t\t"texcoord$0",
\t\t"normal$0",
\t\t"tangent$0"
\t]
\t"jointCount" "int" "0"
\t"flipVCoordinates" "bool" "0"
\t"position$0" "vector3_array"
\t[
${list(pos.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"position$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"texcoord$0" "vector2_array"
\t[
${list(uv.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"texcoord$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"normal$0" "vector3_array"
\t[
${list(nrm.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"normal$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
\t"tangent$0" "vector4_array"
\t[
${list(pos.map(() => "1 0 0 1"), "\t\t")}
\t]
\t"tangent$0Indices" "int_array"
\t[
${list(seq, "\t\t")}
\t]
}
`;
}
const hullNode = (key, file = `${COL_MODEL}_hull_${key}.dmx`) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "PhysicsHullFile"
\t\t\t\t\t\tname = "hull_${key}"
\t\t\t\t\t\tparent_bone = ""
\t\t\t\t\t\tsurface_prop = "default"
\t\t\t\t\t\tcollision_tags = "solid"
\t\t\t\t\t\trecenter_on_parent_bone = false
\t\t\t\t\t\toffset_origin = [ 0.0, 0.0, 0.0 ]
\t\t\t\t\t\toffset_angles = [ 0.0, 0.0, 0.0 ]
\t\t\t\t\t\talign_origin_x_type = "None"
\t\t\t\t\t\talign_origin_y_type = "None"
\t\t\t\t\t\talign_origin_z_type = "None"
\t\t\t\t\t\tfilename = "${file}"
\t\t\t\t\t\timport_scale = 1.0
\t\t\t\t\t\tfaceMergeAngle = 10.0
\t\t\t\t\t\tmaxHullVertices = 0
\t\t\t\t\t\timport_mode = "SingleHull"
\t\t\t\t\t\toptimization_algorithm = "QEM"
\t\t\t\t\t\timport_filter =
\t\t\t\t\t\t{
\t\t\t\t\t\t\texclude_by_default = false
\t\t\t\t\t\t\texception_list = [  ]
\t\t\t\t\t\t}
\t\t\t\t\t},`;
const colVmdlFor = (dmx, hulls) => colVmdlText.replace("__DMX__", dmx).replace("__HULLS__", hulls.map(([k, fl]) => hullNode(k, fl)).join("\n"));
const colVmdlText = `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "goal_net_collision"
\t\t\t\t\t\tfilename = "__DMX__"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "PhysicsShapeList"
\t\t\t\tchildren =
\t\t\t\t[
__HULLS__
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${COL_MODEL}.dmx`, staticDmx("goal_net_collision", Object.values(colSlabs), "materials/soccermod/stadium/goal_frame"));
for (const [key, c] of Object.entries(colSlabs)) write(`${COL_MODEL}_hull_${key}.dmx`, staticDmx(`hull_${key}`, [c], "materials/soccermod/stadium/goal_frame"));
write(`${COL_MODEL}.vmdl`, colVmdlFor(`${COL_MODEL}.dmx`, Object.keys(colSlabs).map((k) => [k, `${COL_MODEL}_hull_${k}.dmx`])));
// side slab model (render mesh hidden with EF_NODRAW, one hull)
write(`${COL_MODEL}_side.dmx`, staticDmx("goal_net_collision_side", [colSideSlab], "materials/soccermod/stadium/goal_frame"));
write(`${COL_MODEL}_side_hull.dmx`, staticDmx("hull_side", [colSideSlab], "materials/soccermod/stadium/goal_frame"));
write(`${COL_MODEL}_side.vmdl`, colVmdlFor(`${COL_MODEL}_side.dmx`, [["side", `${COL_MODEL}_side_hull.dmx`]]));
write(`${MODEL}.dmx`, meshDmx());
write(`${MODEL}_anims/idle.dmx`, animDmx("idle", [new Map(), new Map()]));
for (const a of anims) write(`${MODEL}_anims/${a.name}.dmx`, animDmx(a.name, a.frames));
write(`${MODEL}.vmdl`, vmdl);
write(`${MAT}.vmat`, vmat);
if (previewFile) fs.writeFileSync(previewFile, JSON.stringify({ fps: FPS, bones: bones.map((b) => ({ name: b.name, pos: b.pos.map((v) => +v.toFixed(2)), n: b.pn ? b.pn.n.map((v) => +v.toFixed(4)) : null })), anims: previewAnims }));
console.log(`dynamic net: ${bones.length} bones, ${positions.length} vertices, ${faces.length} quads, ${anims.length} hit animations (${panels.map((pn) => `${pn.key}: peak ${pn.peak.toFixed(3)}`).join(", ")}) -> ${out}`);
