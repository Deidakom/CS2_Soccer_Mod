// Matchday dressing round the stadium: team entrance, buses, TV trucks, fan zone, food trucks,
// monument, pitch-side kit (props/team_entrance.py, team_bus.py, ob_van.py, fan_screen.py, ...).
import { Img, makeNoise, shade, flat, metalPanels, steel, aiTexture } from "../matlib.mjs";

const HOME = [186, 20, 28], AWAY = [28, 64, 176];
// LED wall: a fine RGB pixel grid over a stadium-blue picture with a lit band
const led = () => {
  const img = new Img(512, 256), n = makeNoise(801);
  img.fill((x, y, u, v) => {
    const base = v > 0.38 && v < 0.62 ? [236, 236, 240] : mix3([20, 50, 140], [150, 20, 40], u);
    const sub = Math.floor(x * 3) % 3, cell = (x % 1 < 0.75 && y % 1 < 0.75) ? 1 : 0.55;
    const c = base.map((ch, k) => ch * (k === sub ? 1.15 : 0.9) * cell * (0.95 + n(u, v, 64) * 0.1));
    return c;
  });
  return { color: img, illum: new Img(512, 256).fill(() => [255, 255, 255]) };
};
const mix3 = (a, b, t) => a.map((v, k) => v + (b[k] - v) * t);
// bronze: warm metal with darker patina in the hollows
const bronze = (seed, dark = 1) => {
  const n = makeNoise(seed), img = new Img(256, 256);
  img.fill((x, y, u, v) => shade([150, 96, 46], dark * (0.8 + n.fbm(u, v, 4, 4) * 0.4) - Math.max(0, n(u, v, 16) - 0.7) * 0.6));
  return { color: img };
};
// stadium map on the info pylons: plan of the bowl with the four gates
const stadiumMap = () => {
  const img = new Img(256, 512);
  img.fill((x, y, u, v) => {
    const dx = (u - 0.5) * 2, dy = (v - 0.55) * 2 * 1.6, r = Math.hypot(dx / 0.8, dy / 1.0);
    if (v < 0.12) return [28, 64, 176];
    if (r < 0.38) return [70, 140, 60];
    if (r < 0.8) return Math.abs(r - 0.6) < 0.015 ? [255, 255, 255] : [150, 154, 162];
    if (r < 0.86) return [60, 62, 68];
    const gate = [[0.9, 0], [-0.9, 0], [0, 0.9], [0, -0.9]].some(([gx, gy]) => Math.hypot(dx / 0.8 - gx, dy - gy) < 0.08);
    return gate ? [214, 176, 60] : [236, 236, 230];
  });
  return { color: img };
};

export default {
  door_steel: { make: () => metalPanels({ base: [72, 76, 84], seed: 802, cols: 1, rows: 6, brushed: 0.08 }), rough: 0.45, metal: 0.6, normal: 1 },
  bus_white: { make: () => flat([238, 238, 236], 803, 0.02), rough: 0.2, metal: 0.25 },
  tv_blue: { make: () => flat([22, 52, 140], 804, 0.03), rough: 0.25, metal: 0.25 },
  truck_paint: { make: () => flat([214, 132, 18], 805, 0.03), rough: 0.25, metal: 0.25 },
  barrier_sign: { make: () => flat([24, 56, 150], 806, 0.03), rough: 0.6 },
  stage_deck: { make: () => metalPanels({ base: [34, 34, 36], seed: 807, cols: 4, rows: 4, brushed: 0.04 }), rough: 0.6, metal: 0.3, normal: 1 },
  stage_step: { make: () => flat([54, 54, 58], 808, 0.05), rough: 0.7 },
  screen_led: { make: aiTexture("screen_led", led), rough: 0.2, illum: 2.4 },
  truss_steel: { make: aiTexture("steel_brushed", () => steel([200, 202, 206], 809)), rough: 0.35, metal: 0.8 },
  parasol_home: { make: () => flat(HOME, 810, 0.05), rough: 0.8, twoSided: true },
  parasol_white: { make: () => flat([240, 240, 236], 811, 0.05), rough: 0.8, twoSided: true },
  bronze: { make: aiTexture("bronze", () => bronze(812), 5), rough: 0.35, metal: 0.9 },
  bronze_dark: { make: () => bronze(813, 0.45), rough: 0.5, metal: 0.8 },
  stadium_map: { make: stadiumMap, rough: 0.3, illum: 0.8 },
  camera_body: { make: () => flat([36, 36, 40], 814, 0.04), rough: 0.4, metal: 0.4 },
  sub_board: { make: () => flat([30, 30, 34], 815, 0.03), rough: 0.5, metal: 0.3 },
};
