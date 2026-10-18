// Locker rooms, entrance, plaza dressing (props/locker_bank.py, crest.py, tree.py, flagpole.py, ...).
import { Img, makeNoise, shade, flat, wood, concrete } from "../matlib.mjs";

const HOME = [186, 20, 28], AWAY = [28, 64, 176];
// a pitch drawn on the board with a few X and O and arrows
const tactics = () => {
  const w = 512, h = 336, img = new Img(w, h);
  const near = (d) => Math.abs(d) < 2;
  const marks = [[150, 120, "x"], [150, 220, "x"], [250, 168, "x"], [360, 100, "o"], [360, 240, "o"], [300, 168, "o"]];
  img.fill((x, y) => {
    let line = near(x - 20) || near(x - w + 20) || near(y - 20) || near(y - h + 20) || near(x - w / 2) || near(Math.hypot(x - w / 2, y - h / 2) - 44)
      || (x < 90 && near(y - 90) && x > 20) || (x < 90 && near(y - 246) && x > 20) || (near(x - 90) && y > 90 && y < 246)
      || (x > w - 90 && near(y - 90) && x < w - 20) || (x > w - 90 && near(y - 246) && x < w - 20) || (near(x - w + 90) && y > 90 && y < 246);
    if (line) return [235, 238, 235];
    for (const [mx, my, k] of marks) {
      const dx = x - mx, dy = y - my;
      if (k === "x" && Math.abs(dx) < 12 && Math.abs(dy) < 12 && (Math.abs(dx - dy) < 2.5 || Math.abs(dx + dy) < 2.5)) return [30, 30, 200];
      if (k === "o" && Math.abs(Math.hypot(dx, dy) - 11) < 2.5) return [210, 30, 30];
    }
    if (y > 160 && y < 176 && x > 170 && x < 240 && Math.abs(y - 168) < 1.6 + (x > 228 ? (240 - x) * 0.4 : 0)) return [20, 20, 20];
    return [44, 120, 62];
  });
  return { color: img };
};
const bark = () => {
  const n = makeNoise(601), img = new Img(128, 256);
  img.fill((x, y, u, v) => shade([84, 62, 44], 0.75 + n(u, v, 12, 3) * 0.4 + (n.fbm(u, v, 4, 3, 0.25) - 0.5) * 0.2));
  return { color: img, height: (x, y, u, v) => n(u, v, 12, 3) * 3 };
};
const leaves = () => {
  const n = makeNoise(602), img = new Img(256, 256);
  img.fill((x, y, u, v) => shade([52, 104, 40], 0.7 + n(u, v, 32) * 0.5 + (n.fbm(u, v, 6, 3) - 0.5) * 0.25));
  return { color: img, height: (x, y, u, v) => n(u, v, 32) * 3 };
};

export default {
  ticket_glow: { make: () => flat([40, 230, 90], 501, 0), rough: 0.3, illum: 3 },
  arena_letters: { make: () => flat([250, 250, 255], 502, 0), rough: 0.3, illum: 3.5 },
  crest_rim: { make: () => flat([214, 176, 60], 503, 0.04), rough: 0.25, metal: 0.8 },
  crest_star: { make: () => flat([214, 176, 60], 504, 0.04), rough: 0.25, metal: 0.8 },
  crest_field: { make: () => flat(HOME, 505, 0.03), rough: 0.5 },
  crest_field_away: { make: () => flat(AWAY, 506, 0.03), rough: 0.5 },
  team_paint: { make: () => flat(HOME, 507, 0.03), rough: 0.4, metal: 0.2 },
  team_paint_away: { make: () => flat(AWAY, 508, 0.03), rough: 0.4, metal: 0.2 },
  locker_wood: { make: () => wood([150, 104, 62], 509), rough: 0.5, normal: 1 },
  boot: { make: () => flat([20, 20, 22], 510, 0.05), rough: 0.35 },
  towel: { make: () => flat([240, 240, 236], 511, 0.08), rough: 0.95 },
  tactics_board: { make: tactics, rough: 0.2 },
  leather_dark: { make: () => flat([22, 28, 48], 512, 0.06), rough: 0.5 },
  concrete_planter: { make: () => concrete({ base: [168, 166, 160], seed: 513, joints: 0, jointsV: 0, stain: 0.6 }), rough: 0.9, normal: 1 },
  soil: { make: () => flat([58, 40, 26], 514, 0.2), rough: 1 },
  bark: { make: bark, rough: 0.95, normal: 1 },
  leaves: { make: leaves, rough: 0.8, normal: 1 },
  flag_home: { make: () => flat(HOME, 515, 0.05), rough: 0.8, twoSided: true },
  flag_away: { make: () => flat(AWAY, 516, 0.05), rough: 0.8, twoSided: true },
};
