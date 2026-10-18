// Outside the stadium: plaza floor, kerbs, lawns, hedges, the gate medallions (tools/arena/world.mjs,
// tools/arena/geometry.mjs uses plaza_stone for the whole plaza).
import { Img, makeNoise, shade, flat, aiTexture } from "../matlib.mjs";

// large granite slabs in a running bond, every slab its own shade, fine speckle, dark joints
const slabs = ({ base, seed, cols = 4, rows = 8, joint = 1.6, speckle = 0.16 }) => {
  const S = 512, n = makeNoise(seed), img = new Img(S, S), cw = S / cols, rh = S / rows;
  const slab = (x, y) => { const r = Math.floor(y / rh), off = (r % 2) * cw / 2, c = Math.floor((((x + off) % S) + S) % S / cw); return [r, c, ((x + off) % cw + cw) % cw, ((y % rh) + rh) % rh]; };
  const shadeOf = (r, c) => { let h = (r * 73856093) ^ (c * 19349663) ^ seed; h = (h ^ (h >>> 13)) * 1274126177; return ((h >>> 0) % 1000) / 1000; };
  img.fill((x, y, u, v) => {
    const [r, c, sx, sy] = slab(x, y), e = Math.min(sx, cw - sx, sy, rh - sy);
    if (e < joint) return shade(base, 0.45);
    const k = 0.86 + shadeOf(r, c) * 0.22 + (n(u, v, 256) - 0.5) * speckle + (n.fbm(u, v, 6, 3) - 0.5) * 0.08 - (e < joint + 1.2 ? 0.08 : 0);
    return shade(base, k);
  });
  return { color: img, height: (x, y) => { const [, , sx, sy] = slab(x, y); return Math.min(sx, cw - sx, sy, rh - sy) < joint ? -2 : 0; } };
};
// lawn: two-tone mown stripes and blades
const lawn = () => {
  const n = makeNoise(711), img = new Img(512, 512);
  img.fill((x, y, u, v) => shade([62, 118, 50], (Math.floor(u * 4) % 2 ? 0.92 : 1.06) + (n(u, v, 256) - 0.5) * 0.3 + (n.fbm(u, v, 8, 3) - 0.5) * 0.12));
  return { color: img, height: (x, y, u, v) => n(u, v, 256) * 2 };
};
// clipped hedge: dense leaf clumps
const hedge = () => {
  const n = makeNoise(712), img = new Img(512, 512);
  img.fill((x, y, u, v) => shade([44, 86, 38], 0.6 + n(u, v, 48) * 0.55 + (n(u, v, 160) - 0.5) * 0.25));
  return { color: img, height: (x, y, u, v) => n(u, v, 48) * 4 + n(u, v, 160) * 1.5 };
};
// gate medallion: granite disc, team-coloured rings, a football in the middle, stars round it
const medallion = () => {
  const S = 1024, n = makeNoise(713), img = new Img(S, S);
  img.fill((x, y, u, v) => {
    const dx = u - 0.5, dy = v - 0.5, r = Math.hypot(dx, dy) * 2, a = Math.atan2(dy, dx);
    const grain = 1 + (n(u, v, 256) - 0.5) * 0.14;
    if (r > 0.985) return shade([60, 60, 64], grain);
    if (r > 0.93) return shade([226, 226, 220], grain);
    if (r > 0.86) return shade(Math.cos(a) > 0 ? [176, 22, 28] : [28, 64, 176], grain);
    if (r > 0.83) return shade([226, 226, 220], grain);
    if (r > 0.62 && r < 0.74) { const star = Math.abs(((a / (2 * Math.PI)) * 16 % 1) - 0.5) < 0.12 && Math.abs(r - 0.68) < 0.035; if (star) return shade([214, 176, 60], grain); }
    if (r < 0.42) {   // the ball: white with dark pentagon patches
      const px = dx / 0.21, py = dy / 0.21, pent = (cx, cy, s) => Math.hypot(px - cx, py - cy) < s;
      const dark = pent(0, 0, 0.32) || [0, 72, 144, 216, 288].some((d) => pent(Math.cos((d - 90) * Math.PI / 180) * 0.78, Math.sin((d - 90) * Math.PI / 180) * 0.78, 0.2));
      return shade(dark ? [30, 30, 34] : [236, 236, 232], grain);
    }
    return shade([96, 98, 104], grain);
  });
  return { color: img };
};

export default {
  plaza_stone: { make: aiTexture("plaza_stone", () => slabs({ base: [176, 172, 164], seed: 701 }), 6), rough: 0.75, normal: 1.2 },
  plaza_granite: { make: () => slabs({ base: [112, 112, 116], seed: 702, cols: 2, rows: 4, speckle: 0.3 }), rough: 0.55, normal: 1.2 },
  plaza_granite_dark: { make: aiTexture("plaza_granite_dark", () => slabs({ base: [62, 62, 66], seed: 703, cols: 2, rows: 2, speckle: 0.3 }), 4), rough: 0.4, normal: 1 },
  plaza_inlay: { make: () => slabs({ base: [150, 34, 38], seed: 704, cols: 8, rows: 1, speckle: 0.2 }), rough: 0.5, normal: 1 },
  lawn_grass: { make: aiTexture("lawn_grass", lawn, 6), rough: 0.95, normal: 1 },
  hedge: { make: aiTexture("hedge", hedge, 10), rough: 0.9, normal: 1.4 },
  plaza_medallion: { make: aiTexture("plaza_medallion", medallion), rough: 0.45 },
  city_facade: { make: aiTexture("city_facade", () => flat([190, 186, 178], 714, 0.05), 3), rough: 0.7, normal: 1 },
};
