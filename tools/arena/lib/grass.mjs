// Grass drawn blade by blade (tileable): short strokes leaning with the mowing direction.
// field = brightness of the topmost blade per texel (0 = soil between blades), tone = its hue.
import { Img, makeNoise } from "./img.mjs";

export const GRASS_DARK = [61.6, 82.6, 30.4], GRASS_MID = [66.3, 89.3, 32.5], GRASS_LIGHT = [71.5, 96.6, 35.0];   // measured on v8's grass20

export function bladeField(S, leanAt, count, seed) {
  const field = new Float32Array(S * S), tone = new Float32Array(S * S);
  let s = seed >>> 0; const rnd = () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
  for (let k = 0; k < count; k++) {
    const cx = rnd() * S, cy = rnd() * S, a = leanAt(cx, cy) + (rnd() - 0.5) * 1.5, len = 7 + rnd() * 17, w = 0.8 + rnd() * 1.1, v = 0.35 + rnd() * 0.65, hue = rnd();
    const dx = Math.cos(a), dy = Math.sin(a);
    for (let t = 0; t <= len; t += 0.6) {
      const tip = t / len, px = cx + dx * t, py = cy + dy * t, r = w * (1 - tip * 0.75);
      for (let oy = -1; oy <= 1; oy++) for (let ox = -1; ox <= 1; ox++) {
        if (Math.hypot(ox, oy) > r + 0.3) continue;
        const x = ((Math.round(px) + ox) % S + S) % S, y = ((Math.round(py) + oy) % S + S) % S, i = y * S + x, h = v * (0.55 + 0.45 * tip);
        if (h > field[i]) { field[i] = h; tone[i] = hue; }
      }
    }
  }
  return { field, tone };
}

// colour image from a blade field; baseAt(x, y) -> [r, g, b]
export function grassImage(S, blades, baseAt, seed, patchAmount = 0.07) {
  const n = makeNoise(seed), { field, tone } = blades;
  const img = new Img(S, S).fill((x, y, u, v) => {
    const i = y * S + x, c = baseAt(x, y), patch = n.fbm(u, v, 12, 3) - 0.5, clump = n(u, v, 96) - 0.5;
    const k = (0.5 + field[i] * 0.82 + patch * patchAmount * 2 + clump * 0.1) * 0.943, warm = (tone[i] - 0.5) * 0.14;
    return [c[0] * k * (1 + warm * 1.4), c[1] * k * (1 + warm * 0.3), c[2] * k * (1 - warm)];
  });
  return { color: img, height: (x, y) => field[y * S + x] * 2.2 };
}
