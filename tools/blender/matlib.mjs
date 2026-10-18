// Texture helpers for the prop material files in tools/blender/materials/*.mjs.
import { Img, makeNoise } from "../arena/lib/img.mjs";
import { shade } from "../arena/lib/recipes.mjs";
export { Img, makeNoise, readPng } from "../arena/lib/img.mjs";
export { shade, steel, metalPanels, concrete, tarmac, pavers } from "../arena/lib/recipes.mjs";

// ChatGPT-made pictures (owner 2026-10-18), prepared by tools/blender/ai-textures.mjs. They are not in
// git (2048 PNGs); without the file the material falls back to its generated recipe.
import fs from "node:fs";
import path from "node:path";
import { readPng } from "../arena/lib/img.mjs";
export const AI_DIR = process.env.SM_AI_TEXTURES ?? "C:/Users/sergi/Documents/AI/arena-ai-textures";
// { color, height? } from the picture; bump from brightness (strength in texels per 255) when asked
export const aiTexture = (name, fallback, bump = 0) => () => {
  const file = path.join(AI_DIR, `${name}.png`);
  if (!fs.existsSync(file)) return fallback();
  const color = readPng(file);
  if (!bump) return { color };
  const lum = (x, y) => { const o = ((((Math.round(y) % color.h) + color.h) % color.h) * color.w + (((Math.round(x) % color.w) + color.w) % color.w)) * 4; return (color.d[o] * 0.3 + color.d[o + 1] * 0.59 + color.d[o + 2] * 0.11) / 255; };
  return { color, height: (x, y) => lum(x, y) * bump };
};

// plain colour with a little grain
export const flat = (rgb, seed = 1, grain = 0.04) => {
  const n = makeNoise(seed);
  return { color: new Img(64, 64).fill((x, y, u, v) => shade(rgb, 1 + (n.fbm(u, v, 4, 3) - 0.5) * grain * 2)) };
};
// planks / counter tops: grain along u
export const wood = (rgb, seed) => {
  const n = makeNoise(seed), img = new Img(256, 256);
  img.fill((x, y, u, v) => shade(rgb, 0.85 + 0.25 * Math.sin((u * 18 + n.fbm(u, v, 4, 4, 0.2) * 6) * Math.PI) ** 2 + (n(u, v, 64, 4) - 0.5) * 0.12));
  return { color: img, height: (x, y, u, v) => n(u, v, 64, 4) * 1.5 };
};
