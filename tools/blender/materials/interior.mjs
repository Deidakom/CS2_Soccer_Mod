// Behind the stands: players' tunnels, mixed zone, team rooms, concourse (tools/arena/world.mjs).
import { Img, makeNoise, shade, flat, metalPanels, concrete, aiTexture } from "../matlib.mjs";

const HOME = [176, 22, 28], AWAY = [26, 66, 170];
// square floor or wall tiles with grout
const tiles = (rgb, grout, seed, n = 8, size = 256) => {
  const no = makeNoise(seed), img = new Img(size, size), cell = size / n;
  const g = (x) => Math.min(x % cell, cell - (x % cell));
  img.fill((x, y, u, v) => (Math.min(g(x), g(y)) < 1.5 ? grout : shade(rgb, 0.94 + no(u, v, n, n) * 0.1 + (no.fbm(u, v, 8, 3) - 0.5) * 0.05)));
  return { color: img, height: (x, y) => (Math.min(g(x), g(y)) < 1.5 ? -1.5 : 0) };
};
// ceiling: 2 × 2 panels in a T-bar grid
const ceilingTile = () => {
  const img = new Img(128, 128);
  img.fill((x, y) => (x % 64 < 2 || y % 64 < 2 ? [190, 192, 196] : [226, 226, 222]));
  return { color: img, height: (x, y) => (x % 64 < 2 || y % 64 < 2 ? 1 : 0) };
};
// tunnel walls: dark graphite panels with fine vertical ribs
const graphite = () => {
  const no = makeNoise(31), img = new Img(256, 256);
  img.fill((x, y, u, v) => shade([44, 46, 52], 0.9 + (x % 16 < 2 ? -0.25 : 0) + (no.fbm(u, v, 6, 3) - 0.5) * 0.12));
  return { color: img, height: (x) => (x % 16 < 2 ? -1 : 0) };
};

export default {
  floor_rubber: { make: aiTexture("floor_rubber", () => tiles([52, 58, 70], [40, 44, 52], 21, 4), 4), rough: 0.85, normal: 0.8 },
  floor_concourse: { make: aiTexture("floor_concourse", () => concrete({ base: [150, 150, 146], seed: 22, joints: 2, jointsV: 2, stain: 0.5 }), 3), rough: 0.55, normal: 1 },
  floor_tile: { make: aiTexture("floor_tile", () => tiles([212, 214, 216], [150, 152, 156], 23), 3), rough: 0.35, normal: 1 },
  floor_gym: { make: () => tiles([40, 120, 74], [34, 100, 62], 24, 2), rough: 0.7, normal: 0.6 },
  tunnel_wall: { make: aiTexture("tunnel_wall", graphite, 5), rough: 0.5, metal: 0.2, normal: 1 },
  tunnel_ceiling: { make: () => metalPanels({ base: [30, 31, 35], seed: 25, cols: 2, rows: 2 }), rough: 0.6, normal: 1 },
  strip_home: { make: () => flat(HOME, 26, 0), rough: 0.3, illum: 3 },
  strip_away: { make: () => flat(AWAY, 27, 0), rough: 0.3, illum: 3 },
  ceiling_light: { make: () => flat([255, 250, 236], 28, 0), rough: 0.3, illum: 1.6 },   // 4 burnt the white rooms out
  ceiling_tile: { make: aiTexture("ceiling_tile", ceilingTile, 3), rough: 0.8, normal: 0.5 },
  wall_white: { make: () => flat([170, 171, 172], 29, 0.05), rough: 0.8 },
  wall_home: { make: () => metalPanels({ base: shade(HOME, 0.9), seed: 30, cols: 1, rows: 1, brushed: 0.02 }), rough: 0.6, normal: 0.6 },
  wall_away: { make: () => metalPanels({ base: shade(AWAY, 0.9), seed: 32, cols: 1, rows: 1, brushed: 0.02 }), rough: 0.6, normal: 0.6 },
  concrete_painted: { make: aiTexture("concrete_wall", () => concrete({ base: [182, 182, 178], seed: 33, joints: 4, jointsV: 2, stain: 0.3 }), 5), rough: 0.85, normal: 1 },
  concourse_back: { make: () => concrete({ base: [96, 98, 104], seed: 34, joints: 4, jointsV: 2, stain: 0.4 }), rough: 0.85, normal: 1 },
};
