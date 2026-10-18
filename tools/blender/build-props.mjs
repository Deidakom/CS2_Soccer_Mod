#!/usr/bin/env node
// Builds the Blender props into a content addon: runs every tools/blender/props/<name>.py
// headless, writes models/soccermod_world/<name>.dmx (+ _phys.dmx from its COL_* objects) and
// .vmdl, and the materials they use (materials/soccermod_world, from materials.mjs).
//
//   node tools/blender/build-props.mjs --addon <content addon dir> [--only a,b] [--preview <dir>]
//        [--blender "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe"]
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { staticDmx, staticVmdl, dynamicVmdl } from "../arena/lib/dmx.mjs";
import { vmat } from "../arena/lib/vmat.mjs";
import { textureTarget } from "../arena/lib/img.mjs";
import { MATERIALS } from "./materials.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.addon) throw new Error("usage: --addon <content addon dir> [--only a,b] [--preview <dir>]");
const HERE = path.dirname(fileURLToPath(import.meta.url));
const BLENDER = args.blender ?? "C:/Program Files/Blender Foundation/Blender 5.2/blender.exe";
export const MODEL_DIR = "models/soccermod_world", MAT_DIR = "materials/soccermod_world";
const only = args.only?.split(",");
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), "props-"));
const used = new Set();

fs.mkdirSync(path.join(args.addon, MODEL_DIR), { recursive: true });
for (const file of fs.readdirSync(path.join(HERE, "props")).filter((f) => f.endsWith(".py"))) {
  const name = path.basename(file, ".py");
  if (only && !only.includes(name)) continue;
  const json = path.join(tmp, `${name}.json`), extra = args.preview ? [path.join(args.preview, `${name}.png`)] : [];
  execFileSync(BLENDER, ["-b", "--factory-startup", "-P", path.join(HERE, "props", file), "--", json, ...extra], { stdio: ["ignore", "ignore", "inherit"] });
  if (args.keep) { fs.mkdirSync(args.keep, { recursive: true }); fs.copyFileSync(json, path.join(args.keep, `${name}.json`)); }   // render-map.py
  const { faces, collision, bounds, variants = {} } = JSON.parse(fs.readFileSync(json, "utf8"));
  const dir = path.join(args.addon, MODEL_DIR);
  // the model itself plus one per variant (same geometry, some materials swapped)
  for (const [model, swap] of [[name, {}], ...Object.entries(variants).map(([suffix, s]) => [`${name}_${suffix}`, s])]) {
    const out = faces.map((f) => {
      const m = swap[f.material] ?? f.material;
      if (!MATERIALS[m]) throw new Error(`${model}: material "${m}" is not in tools/blender/materials/*.mjs`);
      return { ...f, material: `${MAT_DIR}/${m}.vmat` };
    });
    fs.writeFileSync(path.join(dir, `${model}.dmx`), staticDmx(model, out));
    if (collision.length) fs.writeFileSync(path.join(dir, `${model}_phys.dmx`), staticDmx(`${model}_phys`, collision.map((f) => ({ ...f, ns: undefined, uvs: [[0, 0], [1, 0], [0, 1]], material: `${MAT_DIR}/steel_dark.vmat` }))));
    // ball_*: physics props (prop_physics) with a sphere of the ball's radius instead of a mesh shape
    const vmdl = name.startsWith("ball_") ? dynamicVmdl(MODEL_DIR, model, ((bounds[1][0] - bounds[0][0]) / 2).toFixed(2), "rubber") : staticVmdl(MODEL_DIR, model, collision.length > 0, "metal");
    fs.writeFileSync(path.join(dir, `${model}.vmdl`), vmdl);
  }
  const vs = Object.keys(variants);
  console.log(`${MODEL_DIR}/${name}: ${faces.length} tris, ${collision.length} collision tris, ${bounds[0].map(Math.round)} .. ${bounds[1].map(Math.round)}${vs.length ? `, variants ${vs.join(" ")}` : ""}`);
}
// every material is written: the generated world shells (tools/arena/world.mjs) use them too
for (const n of Object.keys(MATERIALS)) used.add(n);

fs.mkdirSync(path.join(args.addon, MAT_DIR), { recursive: true });
textureTarget(Number(args.tex ?? 2048));   // owner 2026-10-18: maximum texture resolution (--tex 0 = recipe sizes)
for (const name of used) {
  const m = MATERIALS[name], r = m.make(), has = { normal: !!(m.normal && r.height), alpha: !!r.alpha, illum: !!r.illum };
  const d = path.join(args.addon, MAT_DIR);
  r.color.png(path.join(d, `${name}_color.png`), 3);
  if (has.normal) r.color.setHeight(r.height).normalMap(m.normal).png(path.join(d, `${name}_normal.png`), 3);
  if (r.alpha) r.alpha.png(path.join(d, `${name}_trans.png`), 1);
  if (r.illum) r.illum.png(path.join(d, `${name}_illum.png`), 1);
  fs.writeFileSync(path.join(d, `${name}.vmat`), vmat(MAT_DIR, name, m, has));
}
console.log(`${used.size} materials in ${MAT_DIR}`);
fs.rmSync(tmp, { recursive: true, force: true });
