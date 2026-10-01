#!/usr/bin/env node
// SoccerMod Arena radar: the pitch part is our stadium radar unchanged (same pitch, same frame:
// x / y +-1715, 3.349609375 units per pixel, 1024 px); everything outside the pitch walls is
// redrawn from the arena layout (turf surround, apron, the wall and the first rows of the long sides).
//
//   node tools/arena/generate-arena-radar.mjs <content addon dir>
import fs from "node:fs";
import path from "node:path";
import { Img, readPng } from "./lib/img.mjs";
import { CURVE, PITCH, PARAPET, WALK0, LOWER, MAP_NAME } from "./layout.mjs";

const addon = process.argv[2];
if (!addon) { console.error("usage: generate-arena-radar.mjs <content addon dir>"); process.exit(1); }
const dir = path.join(addon, "panorama/images/overheadmaps"), MAP = MAP_NAME;
const base = readPng(path.join(dir, "soccer_soccermod_stadium_radar.png"));
const N = base.w, ORIGIN = 1715, SCALE = (2 * ORIGIN) / N;
const sup = (x, y, a, b, e) => (Math.abs(x) / a) ** e + (Math.abs(y) / b) ** e;
// distance outside the front curve, measured along the curve normal (good enough for a radar)
function outside(x, y) {
  const { A, B, EXP } = CURVE, s = sup(x, y, A, B, EXP) ** (1 / EXP);
  let nx = Math.abs(x) ** (EXP - 1) / A ** EXP, ny = Math.abs(y) ** (EXP - 1) / B ** EXP; const l = Math.hypot(nx, ny) || 1; nx /= l; ny /= l;
  return (1 - 1 / s) * (Math.abs(x) * nx + Math.abs(y) * ny);
}
const out = new Img(N, N).fill((i, j) => {
  const x = -ORIGIN + (i + 0.5) * SCALE, y = ORIGIN - (j + 0.5) * SCALE;
  if (Math.abs(x) <= PITCH.x + 7 && Math.abs(y) <= PITCH.y + 7) return base.get(i, j);
  if (sup(x, y, 1452, 1868, 9) < 1) return [44, 92, 46, 255];                 // turf surround
  const d = outside(x, y);
  if (d < 0) return [58, 60, 66, 255];                                          // apron
  if (d < PARAPET.depth) return [226, 228, 232, 255];                           // the wall round the pitch
  if (d < WALK0.d1) return [150, 150, 146, 255];
  const row = Math.floor((d - LOWER.d0) / LOWER.depth), end = Math.abs(y) > Math.abs(x) * 1.2;
  const seat = end ? (y > 0 ? [170, 40, 44] : [40, 74, 170]) : [84, 90, 100];
  return [...seat.map((v) => v * (row % 2 ? 0.86 : 1)), 255];
});
out.png(path.join(dir, `${MAP}_radar.png`), 4);
fs.writeFileSync(path.join(dir, `${MAP}_radar_psd.vtex`), fs.readFileSync(path.join(dir, "soccer_soccermod_stadium_radar_psd.vtex"), "utf8").replace("soccer_soccermod_stadium_radar.png", `${MAP}_radar.png`));
const ov = path.join(addon, "resource/overviews");
fs.writeFileSync(path.join(ov, `${MAP}.txt`), fs.readFileSync(path.join(ov, "soccer_soccermod_stadium.txt"), "utf8").replaceAll("soccer_soccermod_stadium", MAP));
console.log(`radar ${N}x${N}: ${dir}/${MAP}_radar.png`);
