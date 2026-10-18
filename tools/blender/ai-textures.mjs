#!/usr/bin/env node
// Owner 2026-10-18: textures generated with ChatGPT images (his Plus account) replace the low-detail
// generated ones. This prepares a downloaded picture for the material library:
//  - seamless: the picture is blended with its half-shifted copy towards the edges (the shifted copy
//    is continuous across the wrap), only when the edge mismatch is noticeable or --tile is forced;
//  - resized to 2048 x 2048 (bicubic), the compiler's cap for a plain PNG.
// Output: <ai dir>/<name>.png, read by tools/blender/materials/*.mjs through aiTexture(name)
// (falls back to the generated recipe when the file is missing, so the build never depends on it).
//
//   node tools/blender/ai-textures.mjs <downloaded.png> <name> [--tile 0|1] [--size 2048]
import fs from "node:fs";
import path from "node:path";
import { Img, readPng } from "../arena/lib/img.mjs";
import { AI_DIR } from "./matlib.mjs";

const [src, name] = process.argv.slice(2);
const args = Object.fromEntries(process.argv.slice(4).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!src || !name) throw new Error("usage: ai-textures.mjs <png> <name> [--tile 0|1] [--size 2048]");
const SIZE = Number(args.size ?? 2048);
const img = readPng(src);
const { w, h, d } = img;
const px = (x, y, c) => d[((((y % h) + h) % h) * w + (((x % w) + w) % w)) * 4 + c];

// edge mismatch: mean difference between the left/right and top/bottom border rows
let diff = 0;
for (let y = 0; y < h; y++) for (let c = 0; c < 3; c++) diff += Math.abs(px(0, y, c) - px(w - 1, y, c));
for (let x = 0; x < w; x++) for (let c = 0; c < 3; c++) diff += Math.abs(px(x, 0, c) - px(x, h - 1, c));
diff /= (w + h) * 3;
const tile = args.tile !== undefined ? args.tile === "1" : diff > 6;

const out = new Img(w, h, true);
for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
  const o = (y * w + x) * 4;
  if (!tile) { for (let c = 0; c < 4; c++) out.d[o + c] = d[o + c]; continue; }
  // weight of the shifted copy: 0 in the middle, 1 at the borders (smooth)
  const ex = Math.abs((x + 0.5) / w - 0.5) * 2, ey = Math.abs((y + 0.5) / h - 0.5) * 2, e = Math.max(ex, ey);
  const t = Math.min(1, Math.max(0, (e - 0.55) / 0.4)), k = t * t * (3 - 2 * t);
  for (let c = 0; c < 3; c++) out.d[o + c] = d[o + c] * (1 - k) + px(x + (w >> 1), y + (h >> 1), c) * k;
  out.d[o + 3] = 255;
}
// bicubic (Catmull-Rom) resize, wrapping at the borders so the result stays tileable
const cub = (p0, p1, p2, p3, t) => p1 + 0.5 * t * (p2 - p0 + t * (2 * p0 - 5 * p1 + 4 * p2 - p3 + t * (3 * (p1 - p2) + p3 - p0)));
// pictures (--aspect keep): longest side SIZE, proportions kept; textures: SIZE x SIZE
const keep = args.aspect === "keep", RW = keep ? Math.round(w >= h ? SIZE : SIZE * w / h) : SIZE, RH = keep ? Math.round(h >= w ? SIZE : SIZE * h / w) : SIZE;
const res = new Img(RW, RH, true);
const at = (x, y, c) => out.d[((((y % h) + h) % h) * w + (((x % w) + w) % w)) * 4 + c];
for (let y = 0; y < RH; y++) {
  const sy = (y + 0.5) * h / RH - 0.5, y0 = Math.floor(sy), ty = sy - y0;
  for (let x = 0; x < RW; x++) {
    const sx = (x + 0.5) * w / RW - 0.5, x0 = Math.floor(sx), tx = sx - x0, o = (y * RW + x) * 4;
    for (let c = 0; c < 3; c++) {
      const row = (yy) => cub(at(x0 - 1, yy, c), at(x0, yy, c), at(x0 + 1, yy, c), at(x0 + 2, yy, c), tx);
      res.d[o + c] = Math.max(0, Math.min(255, cub(row(y0 - 1), row(y0), row(y0 + 1), row(y0 + 2), ty)));
    }
    res.d[o + 3] = 255;
  }
}
fs.mkdirSync(AI_DIR, { recursive: true });
res.png(path.join(AI_DIR, `${name}.png`), 3);
console.log(`${name}: ${w}x${h} -> ${RW}x${RH}, edge mismatch ${diff.toFixed(1)}${tile ? " (made seamless)" : ""}`);
