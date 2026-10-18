// Fan shop (props/merch_shop.py): jerseys in the team colours (home red, away blue).
import { Img, makeNoise, shade, flat, concrete } from "../matlib.mjs";

// knitted shirt fabric: fine vertical rib
const fabric = (rgb, seed) => {
  const n = makeNoise(seed), img = new Img(128, 128);
  img.fill((x, y, u, v) => shade(rgb, 0.92 + (x % 4 < 2 ? 0.06 : 0) + (n(u, v, 16) - 0.5) * 0.08));
  return { color: img, height: (x) => (x % 4 < 2 ? 0.6 : 0) };
};
const glass = () => ({ color: new Img(16, 16).fill(() => [170, 196, 210]), alpha: new Img(16, 16).fill(() => [70, 70, 70]) });

export default {
  jersey_home: { make: () => fabric([186, 20, 28], 401), rough: 0.85, normal: 0.6 },
  jersey_away: { make: () => fabric([28, 64, 176], 402), rough: 0.85, normal: 0.6 },
  jersey_trim: { make: () => flat([240, 240, 236], 403, 0.03), rough: 0.8 },
  jersey_number: { make: () => flat([250, 250, 250], 404, 0), rough: 0.6 },
  shop_floor: { make: () => concrete({ base: [120, 122, 128], seed: 405, joints: 4, jointsV: 4, stain: 0.2 }), rough: 0.35, normal: 0.8 },
  shop_wall: { make: () => flat([234, 234, 230], 406, 0.03), rough: 0.7 },
  shop_ceiling: { make: () => flat([228, 228, 226], 407, 0.02), rough: 0.8 },
  shop_glass: { make: glass, rough: 0.05, metal: 0.3, translucent: true, twoSided: true, noShadow: true },
};
