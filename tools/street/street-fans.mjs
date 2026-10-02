// The onlookers of the street arena: 16 x 8 figures (128 x 256 each, 2048 x 2048) in street clothes -
// hoodies, caps, beanies, basketball jerseys, tracksuits, baggy trousers, sneakers. Owner 2026-10-02:
// "they should not look like blue and red, they should look like really from the streets".
// Same sheet layout as the stadium's fan atlas (tools/arena/lib/fans.mjs), so the crowd generator
// works with either: even rows arms down, odd rows arms up; no team colours anywhere.
import { Img } from "../arena/lib/img.mjs";

export const FAN_ATLAS = { size: 2048, cols: 16, rows: 8, cellW: 128, cellH: 256, mixedRed: 8, mixedBlue: 11 };
const SS = 3;

const SKINS = [[244, 206, 176], [232, 186, 150], [208, 156, 116], [176, 124, 86], [140, 94, 62], [104, 68, 46], [78, 52, 38]];
const HAIRS = [[22, 20, 18], [40, 30, 24], [70, 48, 30], [120, 84, 44], [190, 184, 176], [150, 60, 40]];
const TOPS = [[28, 28, 32], [44, 46, 52], [70, 72, 78], [128, 130, 134], [232, 230, 222], [84, 92, 66], [150, 140, 108], [204, 168, 56], [116, 40, 48], [196, 100, 40], [40, 84, 64], [48, 120, 124],
  [96, 62, 120], [222, 208, 180], [104, 76, 54], [176, 150, 116], [86, 104, 130], [236, 206, 64], [60, 60, 66], [150, 152, 150]];
const PRINTS = [[236, 232, 220], [240, 200, 50], [230, 90, 50], [70, 190, 180], [26, 26, 30], [160, 220, 90]];
const PANTS = [[30, 32, 38], [52, 56, 64], [80, 96, 124], [108, 122, 148], [96, 86, 66], [70, 78, 60], [128, 128, 128], [38, 38, 42], [150, 132, 100]];
const SHOES = [[236, 236, 232], [30, 30, 34], [190, 40, 40], [236, 236, 232], [240, 190, 50], [70, 140, 200]];

export function fanAtlas() {
  const A = FAN_ATLAS, W = A.cellW * SS, H = A.cellH * SS;
  const color = new Img(A.size, A.size), alpha = new Img(A.size, A.size);
  let seed = 1977; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296), pick = (a) => a[Math.floor(rnd() * a.length)];
  for (let row = 0; row < A.rows; row++) for (let col = 0; col < A.cols; col++) {
    const buf = new Float32Array(W * H * 4);
    const put = (x, y, c, a = 1) => { x |= 0; y |= 0; if (x < 0 || y < 0 || x >= W || y >= H) return; const o = (y * W + x) * 4; buf[o] = c[0]; buf[o + 1] = c[1]; buf[o + 2] = c[2]; buf[o + 3] = Math.max(buf[o + 3], a); };
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
    const dark = (c, k = 0.7) => c.map((v) => v * k);
    // ---- who is this ----------------------------------------------------------------------------
    const up = row % 2 === 1, skin = pick(SKINS), hair = pick(HAIRS), woman = rnd() < 0.3, kid = rnd() < 0.08;
    // look: 0 hoodie, 1 hoodie with the hood up, 2 big tee with a print, 3 basketball jersey, 4 tracksuit, 5 open jacket over a tee, 6 yellow football shirt
    const look = [0, 0, 1, 1, 2, 2, 2, 3, 3, 4, 4, 5, 5, 6][Math.floor(rnd() * 14)];
    const top = look === 6 ? [240, 206, 60] : pick(TOPS), print = pick(PRINTS), pants = pick(PANTS), shorts = look === 3 ? rnd() < 0.8 : rnd() < 0.22, shoes = pick(SHOES);
    const head = [0, 0, 1, 1, 1, 2, 2, 3, 4, 5, 6][Math.floor(rnd() * 11)];   // 0 hair, 1 cap, 2 beanie, 3 bucket hat, 4 afro, 5 bald, 6 bandana
    const k = (kid ? 0.82 : 0.96 + rnd() * 0.07) * SS;
    const cx = W / 2, foot = H - 6 * SS, Y = (v) => foot - v * k, X = (v) => cx + v * k;   // figure units: 0 = feet, 232 = top of the head
    const sw = (woman ? 26 : 31) * (kid ? 0.9 : 1), hipY = 104, shoulderY = 186, headY = 212, headR = 17, baggy = shorts ? 11 : 11.5 + rnd() * 2.5;
    // legs: baggy trousers or shorts with bare legs and socks; sneakers with a white sole
    for (const s of [-1, 1]) {
      if (shorts) { capsule(X(s * 10), Y(46), X(s * 11), Y(16), 7 * k, skin); rect(X(s * 11 - 7), Y(26), 14 * k, 10 * k, [232, 232, 226], { cx: X(s * 11), hw: 8 * k }); capsule(X(s * 10), Y(hipY - 6), X(s * 11.5), Y(52), (baggy + 1.5) * k, pants); }
      else { capsule(X(s * 10), Y(hipY - 6), X(s * 12), Y(14), baggy * k, pants); if (look === 4) rect(X(s * (12 + baggy) - 3), Y(hipY - 10), 3 * k, (hipY - 26) * k, [232, 232, 226]); }
      ellipse(X(s * 13), Y(6), 12.5 * k, 6.5 * k, shoes); rect(X(s * 13 - 12), Y(3), 25 * k, 3 * k, [240, 240, 236]);
    }
    // torso: a loose fit, longer than a fitted shirt
    const body = top, hem = hipY - (look === 2 || look === 0 || look === 1 ? 16 : 8);
    for (let y = Math.round(Y(shoulderY)); y <= Math.round(Y(hem)); y++) { const t = (Y(hem) - y) / (Y(hem) - Y(shoulderY)), hw = (woman ? 23 + 4 * t : 26 + 5 * t) * (kid ? 0.9 : 1) * k; for (let x = Math.round(X(0) - hw); x <= Math.round(X(0) + hw); x++) put(x, y, shade(body, (x - X(0)) / hw, 1 - t)); }
    ellipse(X(-sw + 6), Y(shoulderY - 3), 11 * k, 9.5 * k, body); ellipse(X(sw - 6), Y(shoulderY - 3), 11 * k, 9.5 * k, body);
    if (look === 0 || look === 1) { rect(X(-13), Y(hipY + 22), 26 * k, 16 * k, dark(top, 0.86), { cx: X(0), hw: 30 * k }); for (const s of [-4, 4]) rect(X(s - 0.8), Y(shoulderY - 2), 1.6 * k, 26 * k, [226, 226, 220]); }   // pocket, drawstrings
    else if (look === 2) { if (rnd() < 0.5) rect(X(-14), Y(170), 28 * k, 26 * k, print, { cx: X(0), hw: 30 * k }); else ellipse(X(0), Y(156), 13 * k, 13 * k, print); }
    else if (look === 3) { rect(X(-9), Y(172), 18 * k, 30 * k, print, { cx: X(0), hw: 30 * k }); for (const s of [-1, 1]) rect(X(s * 21 - 1.5), Y(shoulderY + 4), 3 * k, (shoulderY - hem) * k, print); ellipse(X(0), Y(shoulderY + 2), 9 * k, 7 * k, skin); }
    else if (look === 4) { rect(X(-1), Y(shoulderY), 2 * k, (shoulderY - hem) * k, [226, 226, 220]); rect(X(-16), Y(170), 10 * k, 6 * k, print); }
    else if (look === 5) { rect(X(-9), Y(shoulderY - 2), 18 * k, (shoulderY - hem) * k, pick(PRINTS), { cx: X(0), hw: 30 * k }); }
    else if (look === 6) { rect(X(-28), Y(shoulderY + 3), 56 * k, 4 * k, [40, 140, 76], { cx: X(0), hw: 30 * k }); rect(X(-6), Y(170), 12 * k, 16 * k, [40, 140, 76]); }
    if (rnd() < 0.3) for (let i = -9; i <= 9; i++) put(X(i), Y(shoulderY - 6 - (9 - Math.abs(i)) * 0.9), [236, 196, 70]);   // a chain
    // arms: sleeves or bare, both up when cheering (one holds a phone now and then)
    const bare = look === 3, half = look === 2 || look === 6, sleeve = bare ? skin : body, lower = bare || half ? skin : body;
    if (up) {
      const spread = 26 + rnd() * 14, reach = 236 + rnd() * 10, phone = rnd() < 0.25;
      for (const s of [-1, 1]) { capsule(X(s * (sw - 4)), Y(shoulderY - 2), X(s * (sw + 8)), Y(shoulderY + 22), 8.5 * k, sleeve); capsule(X(s * (sw + 8)), Y(shoulderY + 22), X(s * spread), Y(reach - 8), 6.8 * k, lower); ellipse(X(s * spread), Y(reach), 7.5 * k, 8 * k, skin); if (look === 4) rect(X(s * (sw + 11)), Y(shoulderY + 20), 2.4 * k, 22 * k, [226, 226, 220]); }
      if (phone) rect(X(spread - 5), Y(reach + 14), 10 * k, 17 * k, [24, 24, 28]);
    } else {
      const swing = 4 + rnd() * 6, pocket = (look === 0 || look === 1) && rnd() < 0.5;
      for (const s of [-1, 1]) { capsule(X(s * (sw - 3)), Y(shoulderY - 4), X(s * (sw + swing)), Y(hipY + 34), 8.5 * k, sleeve); capsule(X(s * (sw + swing)), Y(hipY + 34), X(s * (pocket ? 14 : sw + swing - 3)), Y(pocket ? hipY + 14 : hipY + 2), 6.8 * k, lower); if (!pocket) ellipse(X(s * (sw + swing - 3)), Y(hipY - 4), 7 * k, 8 * k, skin); if (look === 4) rect(X(s * (sw + swing + 4)), Y(shoulderY - 6), 2.4 * k, 60 * k, [226, 226, 220]); }
    }
    // neck, head, face
    rect(X(-6), Y(headY - headR + 3), 12 * k, 12 * k, dark(skin, 0.82));
    if (look === 0) ellipse(X(0), Y(shoulderY + 3), 17 * k, 8 * k, dark(top, 0.9));                       // the hood lying on the shoulders
    if (look === 1) ellipse(X(0), Y(headY + 1), headR * k * 1.28, headR * k * 1.4, top);                   // the hood up
    ellipse(X(0), Y(headY), headR * k * 0.92, headR * k * 1.08, skin);
    const shades = rnd() < 0.22;
    for (const s of [-1, 1]) { if (shades) rect(X(s * 6.2 - 5), Y(headY + 5), 10 * k, 5.5 * k, [18, 18, 20]); else { ellipse(X(s * 6.2), Y(headY + 2), 2.3 * k, 2.6 * k, [34, 26, 22], { flat: true }); rect(X(s * 6.2 - 3.5), Y(headY + 8.5), 7 * k, 1.6 * k, dark(hair, 0.8)); } }
    if (shades) rect(X(-3), Y(headY + 4), 6 * k, 1.6 * k, [18, 18, 20]);
    rect(X(-1), Y(headY - 1), 2 * k, 5 * k, dark(skin, 0.8));
    if (up) ellipse(X(0), Y(headY - 8.5), 5 * k, 3.6 * k, [70, 26, 26], { flat: true }); else rect(X(-4), Y(headY - 8), 8 * k, 1.8 * k, [120, 60, 54]);
    if (!woman && rnd() < 0.3) for (let y = Y(headY - 4); y <= Y(headY - 15); y++) for (let x = X(-10); x <= X(10); x++) { const dx = (x - X(0)) / (11 * k), dy = (y - Y(headY - 6)) / (10 * k); if (dx * dx + dy * dy <= 1 && dy > 0.1) put(x, y, dark(hair, 0.9), 1); }   // a beard
    // hair and headwear
    const topY = Y(headY + headR * 1.08), hat = pick(TOPS), dome = (rx, ry, c, lowTo) => { for (let y = topY - 2 * k; y <= Y(headY + lowTo); y++) for (let x = X(-headR - 3); x <= X(headR + 3); x++) { const dx = (x - X(0)) / (headR * k * rx), dy = (y - Y(headY)) / (headR * k * ry); if (dx * dx + dy * dy <= 1) put(x, y, shade(c, dx)); } };
    if (look !== 1) {
      if (head === 1) { dome(1.0, 1.14, hat, 6); const side = rnd() < 0.3 ? -1 : 1; rect(X(side > 0 ? -headR + 1 : -headR - 9), Y(headY + 8), (headR * 2 + 8) * k, 4.5 * k, dark(hat, 0.7)); rect(X(-5), Y(headY + 17), 10 * k, 6 * k, print); }          // snapback
      else if (head === 2) { dome(1.04, 1.2, hat, 4); rect(X(-headR), Y(headY + 9), headR * 2 * k, 6 * k, dark(hat, 0.82), { cx: X(0), hw: headR * k }); }                                                                                 // beanie
      else if (head === 3) { dome(1.0, 1.12, hat, 7); for (let i = -headR - 7; i <= headR + 7; i++) rect(X(i), Y(headY + 8 - Math.abs(i) * 0.12), k, 5 * k, dark(hat, 0.8)); }                                                             // bucket hat
      else if (head === 4) { ellipse(X(0), Y(headY + 9), headR * k * 1.34, headR * k * 1.1, hair); ellipse(X(0), Y(headY), headR * k * 0.92, headR * k * 0.9, skin); for (const s of [-1, 1]) ellipse(X(s * 6.2), Y(headY + 2), 2.3 * k, 2.6 * k, [34, 26, 22], { flat: true }); }   // afro
      else if (head === 6) { dome(1.0, 1.12, hair, 6); rect(X(-headR), Y(headY + 12), headR * 2 * k, 5.5 * k, pick(PRINTS), { cx: X(0), hw: headR * k }); }                                                                               // bandana
      else if (head === 0) { dome(0.98, 1.12, hair, 6); if (woman || rnd() < 0.3) for (const s of [-1, 1]) capsule(X(s * (headR - 2)), Y(headY + 4), X(s * (headR + 2)), Y(headY - (woman ? 30 : 12)), 5 * k, hair); }                      // hair, long or braids
    }
    if (rnd() < 0.16) { for (const s of [-1, 1]) ellipse(X(s * (headR - 1)), Y(headY + 1), 5 * k, 7 * k, [30, 30, 34]); for (let i = -headR; i <= headR; i++) put(X(i), topY - 3 * k - Math.sqrt(Math.max(0, headR * headR - i * i)) * 0.1 * k, [30, 30, 34]); }   // headphones
    // ---- into the atlas ---------------------------------------------------------------------------
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
