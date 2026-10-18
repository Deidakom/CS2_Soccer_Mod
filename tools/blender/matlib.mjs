// Texture helpers for the prop material files in tools/blender/materials/*.mjs.
import { Img, makeNoise } from "../arena/lib/img.mjs";
import { shade } from "../arena/lib/recipes.mjs";
export { Img, makeNoise, readPng } from "../arena/lib/img.mjs";
export { shade, steel, metalPanels, concrete, tarmac, pavers } from "../arena/lib/recipes.mjs";

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
