// Interior details and the fun fields (props/frame_*.py, plant_*.py, weight_rack.py, volley_net.py, ...).
// Pictures art_* are ChatGPT-made (ASSETS.md); without the file they fall back to a plain colour.
import { Img, makeNoise, shade, flat, wood, steel, aiTexture } from "../matlib.mjs";

const HOME = [186, 20, 28], AWAY = [28, 64, 176];
const art = (name, rgb) => ({ make: aiTexture(name, () => flat(rgb, 900, 0.05)), rough: 0.25 });
// woven basket
const wicker = () => {
  const img = new Img(128, 128), n = makeNoise(901);
  img.fill((x, y, u, v) => shade([176, 140, 92], 0.8 + 0.3 * Math.abs(Math.sin((x + (Math.floor(y / 8) % 2) * 8) * Math.PI / 16)) + (n(u, v, 32) - 0.5) * 0.15));
  return { color: img, height: (x, y) => Math.abs(Math.sin((x + (Math.floor(y / 8) % 2) * 8) * Math.PI / 16)) * 2 };
};

export default {
  art_action: art("art_action", [120, 40, 40]),
  art_fans: art("art_fans", [120, 30, 40]),
  art_net: art("art_net", [90, 90, 100]),
  art_stadium: art("art_stadium", [40, 50, 90]),
  art_vintage: art("art_vintage", [120, 120, 120]),
  art_trophy: art("art_trophy", [150, 120, 40]),
  art_poster_red: art("art_poster_red", [160, 30, 30]),
  art_poster_blue: art("art_poster_blue", [30, 60, 160]),
  art_mural: { make: aiTexture("art_mural", () => flat([60, 40, 70], 902, 0.05)), rough: 0.4, illum: 0.6 },
  art_mediawall: art("art_mediawall", [230, 230, 236]),
  frame_black: { make: () => flat([22, 22, 24], 903, 0.03), rough: 0.35, metal: 0.2 },
  frame_alu: { make: aiTexture("steel_brushed", () => steel([190, 192, 196], 904)), rough: 0.3, metal: 0.8 },
  frame_wood: { make: aiTexture("wood", () => wood([110, 70, 40], 905), 4), rough: 0.5, normal: 1 },
  pot_white: { make: () => flat([236, 236, 232], 906, 0.03), rough: 0.3 },
  pot_dark: { make: () => flat([46, 46, 50], 907, 0.05), rough: 0.6 },
  plant_stem: { make: () => flat([70, 104, 46], 908, 0.08), rough: 0.7 },
  plant_leaf: { make: () => flat([44, 110, 52], 909, 0.12), rough: 0.5, twoSided: true },
  extinguisher_red: { make: () => flat([200, 24, 22], 910, 0.03), rough: 0.25, metal: 0.3 },
  exit_green: { make: () => flat([20, 170, 70], 911, 0), rough: 0.3, illum: 2.5 },
  water_blue: { make: () => flat([90, 160, 230], 912, 0.04), rough: 0.1, metal: 0.1 },
  ball_white: { make: () => flat([240, 240, 236], 913, 0.03), rough: 0.35 },
  ball_orange: { make: () => flat([214, 104, 30], 914, 0.08), rough: 0.6 },
  net_cord: { make: () => flat([236, 236, 236], 915, 0.04), rough: 0.8 },
  cone_orange: { make: () => flat([246, 110, 20], 916, 0.03), rough: 0.5 },
  cone_yellow: { make: () => flat([240, 214, 30], 917, 0.03), rough: 0.5 },
  wicker: { make: wicker, rough: 0.8, normal: 1 },
  ice: { make: () => flat([210, 236, 250], 918, 0.05), rough: 0.05, metal: 0.1 },
  rubber_black: { make: () => flat([24, 24, 26], 919, 0.05), rough: 0.8 },
  glass_shelf: { make: () => flat([200, 220, 228], 920, 0), rough: 0.05, metal: 0.3 },
  trophy_gold: { make: () => flat([226, 178, 60], 921, 0.03), rough: 0.15, metal: 1 },
  crate_blue: { make: () => flat([30, 70, 170], 922, 0.04), rough: 0.6 },
  canopy_home: { make: () => flat(HOME, 923, 0.05), rough: 0.7, twoSided: true },
  canopy_away: { make: () => flat(AWAY, 924, 0.05), rough: 0.7, twoSided: true },
  line_white: { make: () => flat([236, 236, 230], 929, 0.05), rough: 0.7 },
  goal_bar: { make: () => flat([240, 240, 236], 930, 0.02), rough: 0.4 },
  bar_lit: { make: () => flat([255, 220, 60], 931, 0), rough: 0.3, illum: 3 },
  pad_glow: { make: () => flat([40, 200, 255], 932, 0), rough: 0.3, illum: 3 },
  board_white: { make: () => flat([236, 236, 232], 925, 0.03), rough: 0.6 },
  pad_red: { make: () => flat(HOME, 926, 0.05), rough: 0.6 },
  sand: { make: aiTexture("sand", () => flat([222, 200, 160], 927, 0.1), 6), rough: 0.95, normal: 1 },
  court: { make: aiTexture("court", () => flat([40, 120, 120], 928, 0.08), 3), rough: 0.7, normal: 0.6 },
};
