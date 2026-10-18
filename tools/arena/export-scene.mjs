#!/usr/bin/env node
// Offline look at the arena without CS2 (which only draws in the foreground): writes the stadium,
// the world shells and the placed props as one JSON for tools/blender/render-map.py.
//
//   node tools/arena/export-scene.mjs --addon <content addon> --props <build-props --keep dir> --out <scene.json>
import fs from "node:fs";
import path from "node:path";
import { buildArena } from "./geometry.mjs";
import { buildWorld, placedProps } from "./world.mjs";
import { readPng } from "./lib/img.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
const groups = {};
const add = (faces) => { for (const f of faces) { const g = (groups[f.material] ??= []); for (let k = 1; k + 1 < f.pts.length; k++) g.push(...f.pts[0], ...f.pts[k], ...f.pts[k + 1]); } };
add(buildArena().scene.faces);
add(buildWorld().scene.faces);
// average colour of each material's _color.png (the vmat's own picture)
const colour = {};
for (const m of Object.keys(groups)) {
  const png = path.join(args.addon, m.replace(/\.vmat$/, "_color.png"));
  try {
    const img = readPng(png); let r = 0, g = 0, b = 0; const n = img.w * img.h;
    for (let i = 0; i < n; i++) { r += img.d[i * 4]; g += img.d[i * 4 + 1]; b += img.d[i * 4 + 2]; }
    colour[m] = [r / n / 255, g / n / 255, b / n / 255];
  } catch { colour[m] = [0.5, 0.5, 0.5]; }
}
// prop materials too (soccermod_world)
const propColour = {};
const wdir = path.join(args.addon, "materials/soccermod_world");
for (const f of fs.readdirSync(wdir).filter((x) => x.endsWith("_color.png"))) {
  const img = readPng(path.join(wdir, f)); let r = 0, g = 0, b = 0; const n = img.w * img.h;
  for (let i = 0; i < n; i++) { r += img.d[i * 4]; g += img.d[i * 4 + 1]; b += img.d[i * 4 + 2]; }
  propColour[f.replace("_color.png", "")] = [r / n / 255, g / n / 255, b / n / 255];
}
const placed = placedProps().map((p) => ({ model: path.basename(p.model, ".vmdl"), origin: p.origin, yaw: p.yaw }));
fs.writeFileSync(args.out, JSON.stringify({ groups, colour, propColour, placed, propsDir: args.props }));
console.log(`${Object.keys(groups).length} materials, ${placed.length} props -> ${args.out}`);
