// The crowd atlas of the arena: 16 x 8 fan figures (128 x 256 each, 2048 x 2048), drawn with
// shading, faces, hair styles, scarves and jackets. Rows:
//   0 red, arms down      1 red, arms up        2 blue, arms down     3 blue, arms up
//   4 mixed A, arms down  5 mixed A, arms up    6 mixed B, arms down  7 mixed B, arms up
// In the mixed rows column 8 is a red shirt and column 11 a blue one (the plugin's goal clips
// make those jump with their team).
import { Img } from "./img.mjs";

export const FAN_ATLAS = { size: 2048, cols: 16, rows: 8, cellW: 128, cellH: 256, mixedRed: 8, mixedBlue: 11 };
const SS = 3;   // drawn at three times the size, then averaged: soft edges for the cut-out

const SKINS = [[244, 206, 176], [232, 186, 150], [208, 156, 116], [170, 118, 82], [124, 82, 56], [88, 58, 42]];
const HAIRS = [[26, 22, 20], [58, 40, 28], [104, 70, 40], [168, 130, 72], [196, 190, 184], [120, 46, 30]];
const RED = [200, 36, 34], RED_DARK = [140, 22, 22], BLUE = [38, 82, 196], BLUE_DARK = [22, 48, 132], WHITE = [238, 238, 232], BLACK = [30, 32, 38];
const CASUAL = [[70, 86, 110], [110, 72, 46], [44, 46, 54], [222, 220, 210], [206, 174, 60], [56, 116, 74], [128, 132, 138], [196, 110, 44], [34, 44, 86], [132, 52, 98], [96, 150, 186], [190, 170, 140], [78, 92, 64], [150, 60, 54], [84, 70, 110], [60, 130, 130]];
const TROUSERS = [[34, 38, 52], [28, 28, 32], [70, 80, 104], [96, 86, 70], [52, 56, 60]];
// the neutral sides: any shirt with any trousers
const SHIRTS_ANY = [[236, 232, 220], [34, 36, 42], [128, 132, 138], [206, 174, 60], [240, 200, 40], [56, 136, 80], [28, 92, 60], [110, 170, 80], [236, 130, 40], [200, 86, 30], [132, 52, 98], [170, 80, 170],
  [240, 150, 180], [96, 150, 186], [60, 180, 190], [30, 120, 130], [110, 72, 46], [150, 110, 70], [200, 180, 150], [78, 92, 64], [84, 70, 110], [150, 60, 54], [70, 86, 110], [220, 70, 60], [60, 100, 200], [250, 250, 250]];
const TROUSERS_ANY = [[34, 38, 52], [28, 28, 32], [70, 80, 104], [96, 86, 70], [52, 56, 60], [196, 182, 150], [120, 124, 130], [74, 84, 60], [110, 50, 50], [226, 224, 214], [40, 62, 110], [150, 120, 84]];

export function fanAtlas() {
  const A = FAN_ATLAS, W = A.cellW * SS, H = A.cellH * SS;
  const color = new Img(A.size, A.size), alpha = new Img(A.size, A.size);
  let seed = 4711; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296), pick = (a) => a[Math.floor(rnd() * a.length)];
  for (let row = 0; row < A.rows; row++) for (let col = 0; col < A.cols; col++) {
    const buf = new Float32Array(W * H * 4);
    const put = (x, y, c, a = 1) => { x |= 0; y |= 0; if (x < 0 || y < 0 || x >= W || y >= H) return; const o = (y * W + x) * 4; buf[o] = c[0]; buf[o + 1] = c[1]; buf[o + 2] = c[2]; buf[o + 3] = Math.max(buf[o + 3], a); };
    // round shading: lighter towards the middle of the shape, a little darker downwards
    const shade = (c, t, v = 0) => { const k = 0.74 + 0.34 * Math.cos(Math.min(1, Math.abs(t)) * Math.PI / 2) - v * 0.1; return [c[0] * k, c[1] * k, c[2] * k]; };
    const ellipse = (cx, cy, rx, ry, c, opt = {}) => { for (let y = Math.floor(cy - ry); y <= cy + ry; y++) for (let x = Math.floor(cx - rx); x <= cx + rx; x++) { const dx = (x - cx) / rx, dy = (y - cy) / ry; if (dx * dx + dy * dy <= 1) put(x, y, opt.flat ? c : shade(c, dx * 0.9, (dy + 1) / 2)); } };
    const capsule = (x1, y1, x2, y2, r, c) => {
      const dx = x2 - x1, dy = y2 - y1, l2 = dx * dx + dy * dy || 1;
      for (let y = Math.floor(Math.min(y1, y2) - r); y <= Math.max(y1, y2) + r; y++) for (let x = Math.floor(Math.min(x1, x2) - r); x <= Math.max(x1, x2) + r; x++) {
        const t = Math.max(0, Math.min(1, ((x - x1) * dx + (y - y1) * dy) / l2)), qx = x1 + t * dx - x, qy = y1 + t * dy - y, d = Math.hypot(qx, qy);
        if (d <= r) put(x, y, shade(c, (d / r) * (qx * dy - qy * dx > 0 ? 1 : -1) * 0.95, t));
      }
    };
    const rect = (x, y, w, h, c, mid) => { for (let j = 0; j < h; j++) for (let i = 0; i < w; i++) put(x + i, y + j, mid === undefined ? c : shade(c, (x + i - mid.cx) / mid.hw, j / h)); };
    // ---- who is this ----------------------------------------------------------------------------
    const group = row < 2 ? 0 : row < 4 ? 1 : 2, up = row % 2 === 1, team = group === 0 ? RED : group === 1 ? BLUE : null, teamDark = group === 0 ? RED_DARK : BLUE_DARK;
    const skin = pick(SKINS), hair = pick(HAIRS), hairStyle = Math.floor(rnd() * 6), child = rnd() < 0.1, woman = rnd() < 0.3;
    let shirt, look = Math.floor(rnd() * 5), scarf = false, jacket = null;
    if (team) { const neutral = col >= 14; shirt = neutral ? pick([WHITE, BLACK, [120, 124, 130]]) : team; scarf = !neutral && rnd() < 0.4; if (rnd() < 0.22) jacket = pick([BLACK, [52, 56, 64], teamDark]); if (neutral) look = 0; }
    else { shirt = col === A.mixedRed ? RED : col === A.mixedBlue ? BLUE : pick(SHIRTS_ANY); if (rnd() < 0.3) jacket = pick(SHIRTS_ANY); look = col === A.mixedRed || col === A.mixedBlue ? 0 : look; }
    const trousers = pick(team ? TROUSERS : TROUSERS_ANY), k = (child ? 0.82 : 0.96 + rnd() * 0.07) * SS;
    const cx = W / 2, foot = H - 6 * SS, Y = (v) => foot - v * k, X = (v) => cx + v * k;   // figure units: 0 = feet, 232 = top of the head
    const sw = (woman ? 25 : 29) * (child ? 0.9 : 1), hipY = 104, shoulderY = 186, headY = 212, headR = 17;
    // legs, shoes
    for (const s of [-1, 1]) { capsule(X(s * 10), Y(hipY - 6), X(s * 11), Y(14), 9 * k, trousers); ellipse(X(s * 12), Y(6), 11 * k, 6 * k, [40, 40, 44]); }
    // torso
    const body = jacket ?? shirt;
    for (let y = Math.round(Y(shoulderY)); y <= Math.round(Y(hipY - 8)); y++) { const t = (Y(hipY - 8) - y) / (Y(hipY - 8) - Y(shoulderY)), hw = (woman ? 21 + 5 * t : 22 + 7 * t) * (child ? 0.9 : 1) * k; for (let x = Math.round(X(0) - hw); x <= Math.round(X(0) + hw); x++) put(x, y, shade(body, (x - X(0)) / hw, 1 - t)); }
    ellipse(X(-sw + 6), Y(shoulderY - 3), 10 * k, 9 * k, body); ellipse(X(sw - 6), Y(shoulderY - 3), 10 * k, 9 * k, body);
    if (jacket) { rect(X(-6), Y(shoulderY - 2), 12 * k, (shoulderY - hipY + 4) * k, shirt, { cx: X(0), hw: 30 * k }); rect(X(-1), Y(shoulderY), 2 * k, (shoulderY - hipY + 6) * k, [20, 20, 24]); }
    else if (look === 1) for (const s of [-12, 0, 12]) rect(X(s - 3), Y(shoulderY - 4), 6 * k, (shoulderY - hipY) * k, team ? WHITE : [...shirt].map((c) => c * 0.72));   // stripes
    else if (look === 2) for (const v of [130, 152, 174]) rect(X(-26), Y(v), 52 * k, 8 * k, team ? WHITE : [...shirt].map((c) => c * 0.72), { cx: X(0), hw: 28 * k });   // hoops
    else if (look === 3) rect(X(-12), Y(166), 24 * k, 16 * k, team ? WHITE : pick(CASUAL), { cx: X(0), hw: 28 * k });   // crest / print
    // arms and hands
    const sleeve = body, hand = skin;
    if (up) {
      const spread = 26 + rnd() * 14, reach = 236 + rnd() * 10;
      for (const s of [-1, 1]) { capsule(X(s * (sw - 4)), Y(shoulderY - 2), X(s * (sw + 8)), Y(shoulderY + 22), 8 * k, sleeve); capsule(X(s * (sw + 8)), Y(shoulderY + 22), X(s * spread), Y(reach - 8), 6.5 * k, look === 4 || jacket ? sleeve : skin); ellipse(X(s * spread), Y(reach), 7.5 * k, 8 * k, hand); }
      if (scarf) { rect(X(-spread - 4), Y(reach + 9), (2 * spread + 8) * k, 15 * k, team); for (let i = 0; i < 7; i++) rect(X(-spread + 2 + i * (2 * spread / 7)), Y(reach + 9), 4 * k, 15 * k, WHITE); }
    } else {
      const swing = 4 + rnd() * 6;
      for (const s of [-1, 1]) { capsule(X(s * (sw - 3)), Y(shoulderY - 4), X(s * (sw + swing)), Y(hipY + 34), 8 * k, sleeve); capsule(X(s * (sw + swing)), Y(hipY + 34), X(s * (sw + swing - 3)), Y(hipY + 2), 6.5 * k, look === 4 || jacket ? sleeve : skin); ellipse(X(s * (sw + swing - 3)), Y(hipY - 4), 7 * k, 8 * k, hand); }
    }
    // neck, head, face
    rect(X(-6), Y(headY - headR + 3), 12 * k, 12 * k, skin.map((c) => c * 0.82));
    ellipse(X(0), Y(headY), headR * k * 0.92, headR * k * 1.08, skin);
    if (scarf && !up) { rect(X(-15), Y(shoulderY + 6), 30 * k, 11 * k, team, { cx: X(0), hw: 18 * k }); for (const s of [-9, 0, 9]) rect(X(s - 2), Y(shoulderY + 6), 4 * k, 11 * k, WHITE); rect(X(4), Y(shoulderY - 4), 10 * k, 30 * k, team, { cx: X(9), hw: 7 * k }); }
    for (const s of [-1, 1]) { ellipse(X(s * 6.2), Y(headY + 2), 2.3 * k, 2.6 * k, [34, 26, 22], { flat: true }); rect(X(s * 6.2 - 3.5), Y(headY + 8.5), 7 * k, 1.6 * k, hair.map((c) => c * 0.8)); }
    rect(X(-1), Y(headY - 1), 2 * k, 5 * k, skin.map((c) => c * 0.8));
    if (up) ellipse(X(0), Y(headY - 8.5), 5 * k, 3.6 * k, [70, 26, 26], { flat: true }); else rect(X(-4), Y(headY - 8), 8 * k, 1.8 * k, [120, 60, 54]);
    // hair / headwear
    const top = Y(headY + headR * 1.08), hat = team ?? pick(SHIRTS_ANY);
    if (hairStyle === 4) { for (let y = top - k; y <= Y(headY + 5); y++) for (let x = X(-headR); x <= X(headR); x++) { const dx = (x - X(0)) / (headR * k * 0.98), dy = (y - Y(headY)) / (headR * k * 1.12); if (dx * dx + dy * dy <= 1) put(x, y, shade(hat, dx)); } rect(X(-headR + 2), Y(headY + 7), (headR * 2 + 8) * k, 4 * k, hat.map((c) => c * 0.7)); }   // cap
    else if (hairStyle === 5) { for (let y = top - 2 * k; y <= Y(headY + 3); y++) for (let x = X(-headR - 1); x <= X(headR + 1); x++) { const dx = (x - X(0)) / (headR * k * 1.02), dy = (y - Y(headY)) / (headR * k * 1.18); if (dx * dx + dy * dy <= 1) put(x, y, shade(hat, dx)); } ellipse(X(0), top - 3 * k, 5 * k, 5 * k, WHITE); }   // bobble hat
    else if (hairStyle !== 3) {
      for (let y = top - k; y <= Y(headY + 6); y++) for (let x = X(-headR); x <= X(headR); x++) { const dx = (x - X(0)) / (headR * k * 0.96), dy = (y - Y(headY)) / (headR * k * 1.12); if (dx * dx + dy * dy <= 1) put(x, y, shade(hair, dx, 0.2)); }
      if (woman || hairStyle === 2) for (const s of [-1, 1]) capsule(X(s * (headR - 2)), Y(headY + 4), X(s * (headR + 1)), Y(headY - (woman ? 26 : 10)), 5 * k, hair);
    }
    // ---- into the atlas: average the 2 x 2 blocks, darken the rim a little -------------------------
    const ox = col * A.cellW, oy = row * A.cellH;
    for (let y = 0; y < A.cellH; y++) for (let x = 0; x < A.cellW; x++) {
      let r = 0, g = 0, b = 0, a = 0;
      for (let j = 0; j < SS; j++) for (let i = 0; i < SS; i++) { const o = ((y * SS + j) * W + x * SS + i) * 4; r += buf[o] * buf[o + 3]; g += buf[o + 1] * buf[o + 3]; b += buf[o + 2] * buf[o + 3]; a += buf[o + 3]; }
      const cover = a / (SS * SS), rim = 0.6 + 0.4 * cover, o = ((oy + y) * A.size + ox + x) * 4;
      color.d.set(a > 0 ? [r / a * rim, g / a * rim, b / a * rim, 255] : [46, 42, 44, 255], o);
      alpha.d.set([cover * 255, cover * 255, cover * 255, 255], o);
    }
  }
  // bleed the figures' colours outwards so mip maps do not pull in the background colour
  for (let pass = 0; pass < 6; pass++) {
    const src = color.d.slice();
    for (let y = 1; y < A.size - 1; y++) for (let x = 1; x < A.size - 1; x++) {
      const o = (y * A.size + x) * 4; if (alpha.d[o] > 0 || src[o + 3] === 254) continue;
      let r = 0, g = 0, b = 0, n = 0;
      for (const d of [-4, 4, -A.size * 4, A.size * 4]) if (alpha.d[o + d] > 0 || src[o + d + 3] === 254) { r += src[o + d]; g += src[o + d + 1]; b += src[o + d + 2]; n++; }
      if (n) color.d.set([r / n, g / n, b / n, 254], o);
    }
  }
  return { color, alpha };
}
