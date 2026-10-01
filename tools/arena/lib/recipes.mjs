// Texture recipes shared by the stadium and the indoor hall (moved out of generate-arena-textures.mjs
// 2026-10-01). Each returns { color: Img, height?: fn, alpha?: Img (grey), illum?: Img (grey) }.
import { Img, makeNoise, mix, clamp01, smooth, gridLine, roundRect } from "./img.mjs";
import { bladeField, grassImage, GRASS_DARK, GRASS_MID, GRASS_LIGHT } from "./grass.mjs";

export const shade = (c, k) => [c[0] * k, c[1] * k, c[2] * k];

export function concrete({ size = 512, base, seed, joints = 0, jointsV = 0, ties = false, streaks = 0.5, stain = 1 }) {
  const n = makeNoise(seed), img = new Img(size, size);
  const h = (x, y, u, v) => {
    const pit = Math.max(0, n(u, v, size / 4) - 0.78) * 4;
    const j = joints ? smooth(2.2, 0.4, gridLine(y, size / joints)) : 0, jv = jointsV ? smooth(2.2, 0.4, gridLine(x + (Math.floor(y / (size / (joints || 1))) % 2) * size / jointsV / 2, size / jointsV)) : 0;
    return n.fbm(u, v, 32, 3) * 1.2 - pit * 1.4 - Math.max(j, jv) * 2.2;
  };
  img.fill((x, y, u, v) => {
    const blotch = n.fbm(u, v, 3, 4) - 0.5, grain = n(u, v, size / 2) - 0.5, fine = n(u + 0.31, v + 0.17, size / 4) - 0.5;
    const drip = (n.fbm(u, v, 20, 3, 14) - 0.5) * streaks;
    const dirt = Math.max(0, n.fbm(u + 0.5, v + 0.2, 5, 3) - 0.58) * stain;
    const pit = Math.max(0, n(u, v, size / 4) - 0.8) * 2.2;
    let k = 1 + blotch * 0.2 + grain * 0.07 + fine * 0.07 + drip * 0.18 - dirt * 0.55 - pit * 0.25;
    if (joints) k -= smooth(2.6, 0.5, gridLine(y, size / joints)) * 0.3;
    if (jointsV) k -= smooth(2.6, 0.5, gridLine(x + (Math.floor(y / (size / (joints || 1))) % 2) * size / jointsV / 2, size / jointsV)) * 0.28;
    if (ties) {
      const cx = size / 8, cy = size / (joints * 2 || 4), dx = gridLine(x - cx / 2 + cx, cx * 2), dy = gridLine(y - cy, cy * 2);
      k -= smooth(4.2, 2.2, Math.hypot(dx, dy)) * 0.3;
    }
    return shade(base, k);
  });
  return { color: img, height: h };
}
export function riser() {
  const n = makeNoise(31), img = new Img(256, 64);
  img.fill((x, y, u, v) => {
    const k = 0.9 + (n.fbm(u, v, 8, 3, 0.25) - 0.5) * 0.16 + (n(u, v, 128, 32) - 0.5) * 0.06 - smooth(0.3, 0, v) * 0.28 + smooth(0.75, 1, v) * 0.05;
    return shade([150, 149, 145], k);
  });
  return { color: img };
}
export function stair() {
  const n = makeNoise(44), img = new Img(128, 128);
  img.fill((x, y, u, v) => {
    const tread = v < 0.5, t = tread ? v / 0.5 : (v - 0.5) / 0.5;
    const k = 1 + (n.fbm(u, v, 12, 3) - 0.5) * 0.18 + (n(u, v, 64) - 0.5) * 0.08;
    const yellow = [232, 186, 22];
    if (tread && t < 0.2) return shade(yellow, k * (0.9 + (n(u, v, 32) - 0.5) * 0.25));
    if (!tread && t < 0.14) return shade(yellow, k * 0.86);
    return shade(tread ? [158, 157, 152] : [132, 131, 127], k * (tread ? 1 : 1 - smooth(0.5, 0.14, t) * 0.18));
  });
  return { color: img };
}
export function metalPanels({ size = 256, base, seed, cols = 2, rows = 2, brushed = 0.06 }) {
  const n = makeNoise(seed), img = new Img(size, size);
  const line = (x, y) => Math.min(gridLine(x, size / cols), gridLine(y, size / rows));
  img.fill((x, y, u, v) => {
    const k = 1 + (n(u, v, 4, size / 2) - 0.5) * brushed * 2 + (n.fbm(u, v, 4, 3) - 0.5) * 0.1 - smooth(1.8, 0.3, line(x, y)) * 0.45 + smooth(3.2, 1.8, line(x, y)) * smooth(1.8, 3.2, line(x, y)) * 0.12;
    return shade(base, k);
  });
  return { color: img, height: (x, y) => -smooth(2.4, 0.4, line(x, y)) * 2.5 };
}
export function turf(base, seed) {
  const n = makeNoise(seed), img = new Img(512, 512);
  img.fill((x, y, u, v) => {
    const k = 1 + (n(u, v, 256) - 0.5) * 0.3 + (n(u + 0.4, v + 0.7, 128, 256) - 0.5) * 0.18 + (n.fbm(u, v, 6, 3) - 0.5) * 0.14;
    return shade(base, k);
  });
  return { color: img, height: (x, y, u, v) => n(u, v, 256) * 1.5 };
}
export function tarmac() {
  const n = makeNoise(77), img = new Img(512, 512);
  img.fill((x, y, u, v) => {
    const speck = n(u, v, 256), k = 1 + (speck - 0.5) * 0.4 + (n.fbm(u, v, 5, 4) - 0.5) * 0.22 + Math.max(0, n(u + 0.2, v + 0.6, 128) - 0.82) * 1.6;
    return shade([64, 66, 70], k);
  });
  return { color: img, height: (x, y, u, v) => n(u, v, 256) * 1.2 };
}
export function pavers() {
  const n = makeNoise(91), size = 512, img = new Img(size, size), cell = size / 6;
  const line = (x, y) => Math.min(gridLine(x + (Math.floor(y / cell) % 2) * cell / 2, cell), gridLine(y, cell));
  img.fill((x, y, u, v) => {
    const cx = Math.floor((x + (Math.floor(y / cell) % 2) * cell / 2) / cell), cy = Math.floor(y / cell);
    const tone = ((cx * 7 + cy * 13) % 5) / 5;
    const k = 0.94 + tone * 0.1 + (n(u, v, 256) - 0.5) * 0.1 + (n.fbm(u, v, 4, 3) - 0.5) * 0.14 - smooth(2, 0.4, line(x, y)) * 0.4;
    return shade([170, 166, 158], k);
  });
  return { color: img, height: (x, y) => -smooth(2.4, 0.4, line(x, y)) * 2 };
}
// one tip-up seat per tile: backrest in the top half, the folded seat in the bottom half
export function seat(rgb) {
  const w = 64, h = 128, img = new Img(w, h), alpha = new Img(w, h);
  const dist = (x, y) => (y < h / 2 ? roundRect(x, y, w / 2, h / 4 + 1, w * 0.43, h / 4 - 3, 9) : roundRect(x, y, w / 2, h * 0.75, w * 0.41, h / 4 - 4, 7));
  img.fill((x, y, u, v) => {
    const d = dist(x, y), back = y < h / 2, t = back ? y / (h / 2) : (y - h / 2) / (h / 2);
    let k = back ? 1.08 - t * 0.2 : 0.8 - t * 0.12;                                   // the folded seat shows its darker underside
    k += smooth(-7, -1.5, d) * -0.22 + smooth(-3.5, -1.2, d) * 0.1;                   // rounded rim
    if (back) k -= (smooth(3, 1, Math.abs(y - h * 0.16)) + smooth(3, 1, Math.abs(y - h * 0.3))) * 0.1 * smooth(w * 0.3, w * 0.2, Math.abs(x - w / 2));   // moulded slots
    else k -= smooth(2.2, 0.6, Math.abs(x - w / 2)) * 0.12 * smooth(0.2, 0.3, t);
    return shade(rgb, k);
  });
  alpha.fill((x, y) => { const a = smooth(0.8, -0.8, dist(x, y)) * 255; return [a, a, a]; });
  return { color: img, alpha };
}
export function seatFrame() {
  const w = 64, h = 64, img = new Img(w, h), alpha = new Img(w, h);
  const solid = (x, y) => y < 10 || Math.abs(x - w / 2) < 5 || (y > h - 7 && Math.abs(x - w / 2) < 13);
  img.fill((x, y) => shade([52, 54, 58], 1 - (y / h) * 0.3 + (Math.abs(x - w / 2) < 2 ? 0.2 : 0)));
  alpha.fill((x, y) => (solid(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, alpha };
}
export function ledBand() {
  const w = 64, h = 64, img = new Img(w, h), illum = new Img(w, h);
  const lit = (y) => smooth(5.5, 3.5, y) + smooth(h - 5.5, h - 3.5, y);
  img.fill((x, y) => {
    const dot = smooth(1.3, 0.5, Math.hypot(gridLine(x - 2, 4), gridLine(y - 2, 4))), l = lit(y);
    return mix(shade([16, 17, 20], 1 + dot * 0.9), [150, 225, 255], l);
  });
  illum.fill((x, y) => { const l = lit(y) * 255; return [l, l, l]; });
  return { color: img, illum };
}
export function vipGlass() {
  const w = 128, h = 128, img = new Img(w, h), alpha = new Img(w, h);
  const frame = (x, y) => gridLine(x, w) < 3.5 || y < 4 || y > h - 5;
  img.fill((x, y, u, v) => (frame(x, y) ? shade([40, 42, 46], 1 + (v - 0.5) * 0.2) : mix([70, 104, 124], [110, 150, 172], 1 - v)));
  alpha.fill((x, y) => (frame(x, y) ? [255, 255, 255] : [88, 88, 88]));
  return { color: img, alpha };
}
export function vipInterior() {
  const w = 1024, h = 256, n = makeNoise(121), img = new Img(w, h);
  img.fill((x, y, u, v) => {
    // warm timber wall, a row of ceiling spots, a lit bar, people as dark shapes
    const bay = (x % 256) / 256, wood = 1 + (n.fbm(u, v, 40, 3, 0.1) - 0.5) * 0.25;
    let c = shade([150, 96, 54], wood * (1.05 - v * 0.55));
    if (gridLine(x, 256) < 4) c = [40, 30, 24];
    const spot = Math.exp(-(((bay - 0.5) / 0.16) ** 2)) * smooth(0.5, 0, v);
    c = mix(c, [255, 226, 170], spot * 0.85);
    if (v > 0.62 && v < 0.74) c = mix(c, [255, 205, 130], 0.75 * smooth(0.02, 0.1, Math.abs(bay - 0.5) < 0.38 ? 0.2 : 0));   // bar light
    if (v >= 0.74) c = shade([70, 46, 30], 1 - (v - 0.74) * 1.4);
    // figures
    for (let k = 0; k < 3; k++) {
      const cx = (Math.floor(x / 256) * 256) + 50 + k * 78 + (n.rndAt?.(k) ?? 0), px = x - cx, head = Math.hypot(px, y - 150 + (k % 2) * 8);
      if (head < 13 || (Math.abs(px) < 17 && y > 164 - (k % 2) * 8)) c = shade([36, 30, 34], 1 + k * 0.12);
    }
    return c;
  });
  return { color: img };
}
export function concourse() {
  const w = 1024, h = 256, n = makeNoise(133), img = new Img(w, h), illum = new Img(w, h);
  const kiosk = (x, y) => { const bx = x % 512; return bx > 60 && bx < 300 && y > 70 && y < 190; };
  const menu = (x, y) => { const bx = x % 512; return bx > 70 && bx < 290 && y > 78 && y < 112; };
  const door = (x, y) => { const bx = x % 512; return bx > 370 && bx < 450 && y > 60; };
  const sign = (x, y) => y > 14 && y < 40;
  img.fill((x, y, u, v) => {
    let c = shade([58, 60, 66], 1 + (n.fbm(u, v, 6, 3) - 0.5) * 0.2 - v * 0.2);
    if (sign(x, y)) c = Math.floor(x / 512) % 2 ? [200, 44, 48] : [46, 96, 214];
    if (kiosk(x, y)) c = mix([255, 214, 150], [226, 150, 84], (y - 70) / 120);
    if (menu(x, y)) c = ((Math.floor((x % 512 - 70) / 44) % 2) ? [250, 250, 244] : [255, 232, 180]);
    if (kiosk(x, y) && y > 150) c = shade([92, 66, 46], 1);                       // counter
    if (door(x, y)) c = [16, 17, 20];
    // people in front of the kiosk
    const bx = x % 512;
    for (const [cx, hh] of [[110, 0], [176, 9], [236, 3], [330, 6]]) { const px = bx - cx; if (Math.hypot(px, y - 150 + hh) < 12 || (Math.abs(px) < 15 && y > 163 - hh)) c = [30, 30, 36]; }
    return c;
  });
  illum.fill((x, y) => { const bx = x % 512; const on = (kiosk(x, y) && y <= 150) || sign(x, y); let people = false; for (const [cx, hh] of [[110, 0], [176, 9], [236, 3], [330, 6]]) { const px = bx - cx; if (Math.hypot(px, y - 150 + hh) < 12 || (Math.abs(px) < 15 && y > 163 - hh)) people = true; } const l = on && !people ? 255 : 0; return [l, l, l]; });
  return { color: img, illum };
}
export function ceilingLights() {
  const s = 128, n = makeNoise(141), img = new Img(s, s), illum = new Img(s, s);
  const lamp = (x, y) => Math.abs(x - s / 2) < 40 && Math.abs(y - s / 2) < 7;
  img.fill((x, y, u, v) => (lamp(x, y) ? [255, 248, 230] : shade([86, 88, 92], 1 + (n(u, v, 64) - 0.5) * 0.1 - smooth(1.5, 0.3, Math.min(gridLine(x, s), gridLine(y, s))) * 0.3)));
  illum.fill((x, y) => (lamp(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, illum };
}
export function portal() {
  const w = 128, h = 256, n = makeNoise(151), img = new Img(w, h), illum = new Img(w, h);
  const door = (x, y) => Math.abs(x - w / 2) < 46 && y > 62;
  const sign = (x, y) => Math.abs(x - w / 2) < 30 && y > 26 && y < 50;
  const arrow = (x, y) => { const ax = x - w / 2, ay = y - 38; return (Math.abs(ay) < 3 && ax > -18 && ax < 12) || (ax >= 6 && ax < 20 && Math.abs(ay) < 20 - ax); };
  img.fill((x, y, u, v) => {
    if (sign(x, y)) return arrow(x, y) ? [245, 255, 245] : [22, 150, 70];
    if (door(x, y)) { const depth = smooth(62, 250, y); return mix([8, 8, 10], [70, 52, 34], depth * 0.9 * smooth(46, 10, Math.abs(x - w / 2))); }   // warm light deep inside
    return shade([146, 146, 142], 1 + (n.fbm(u, v, 8, 3) - 0.5) * 0.2 - smooth(8, 0, Math.abs(Math.abs(x - w / 2) - 46)) * (y > 62 ? 0.3 : 0));
  });
  illum.fill((x, y) => (sign(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, illum };
}
export function roofMetal() {
  const s = 512, n = makeNoise(161), img = new Img(s, s), seam = (x) => gridLine(x, s / 4);
  img.fill((x, y, u, v) => {
    const k = 1 + (n.fbm(u, v, 30, 3, 20) - 0.5) * 0.12 + (n.fbm(u, v, 3, 3) - 0.5) * 0.1 - smooth(3, 1, seam(x - 3)) * 0.22 + smooth(3, 1, seam(x + 2)) * 0.12 - Math.max(0, n.fbm(u + 0.3, v, 6, 3, 6) - 0.62) * 0.5;
    return shade([196, 200, 204], k);
  });
  return { color: img, height: (x) => smooth(4, 0.5, seam(x)) * 5 };
}
export function roofCeiling() {
  const s = 512, n = makeNoise(171), img = new Img(s, s), line = (x, y) => Math.min(gridLine(x, s / 2), gridLine(y, s / 4));
  img.fill((x, y, u, v) => {
    const hole = smooth(1.5, 0.6, Math.hypot(gridLine(x, 8), gridLine(y, 8)));
    return shade([178, 182, 188], 1 + (n.fbm(u, v, 4, 3) - 0.5) * 0.08 - hole * 0.12 - smooth(2.2, 0.4, line(x, y)) * 0.4);
  });
  return { color: img, height: (x, y) => -smooth(2.6, 0.4, line(x, y)) * 2 };
}
export function roofGlass() {
  const s = 256, n = makeNoise(181), img = new Img(s, s), alpha = new Img(s, s);
  const bar = (x, y) => gridLine(x, s) < 5 || gridLine(y, s / 2) < 2.5;
  img.fill((x, y, u, v) => (bar(x, y) ? [206, 210, 214] : shade([196, 222, 238], 1 + (n.fbm(u, v, 4, 3) - 0.5) * 0.12 - smooth(2.5, 0.5, gridLine(x, s / 8)) * 0.05)));
  alpha.fill((x, y, u, v) => (bar(x, y) ? [255, 255, 255] : shade([116, 116, 116], 1 + (n.fbm(u, v, 4, 3) - 0.5) * 0.3)));
  return { color: img, alpha };
}
export function steel(base, seed) {
  const n = makeNoise(seed), img = new Img(128, 128);
  img.fill((x, y, u, v) => shade(base, 1 + (n.fbm(u, v, 6, 3) - 0.5) * 0.1 + (n(u, v, 64) - 0.5) * 0.05 - Math.max(0, n.fbm(u + 0.2, v + 0.4, 9, 3, 5) - 0.66) * 0.5));
  return { color: img };
}
export function truss() {
  const w = 512, h = 256, img = new Img(w, h), alpha = new Img(w, h);
  // two bays per tile: posts and crossing diagonals between the chords
  const member = (x, y) => {
    const bay = w / 2, bx = ((x % bay) + bay) % bay, t = bx / bay, yy = y / h;
    const post = Math.min(bx, bay - bx) < 7, d1 = Math.abs(yy - t) * h * 0.9 < 9, d2 = Math.abs(yy - (1 - t)) * h * 0.9 < 9;
    return post || d1 || d2 || y < 10 || y > h - 11;
  };
  img.fill((x, y) => shade([214, 216, 220], 0.95 + ((x * 7 + y * 3) % 11) / 11 * 0.08));
  alpha.fill((x, y) => (member(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, alpha };
}
export function floodlight() {
  const s = 64, img = new Img(s, s), illum = new Img(s, s);
  const led = (x, y) => Math.hypot(gridLine(x - 8, 16), gridLine(y - 8, 16)) < 5.2 && y > 6 && y < s - 6;
  img.fill((x, y) => (led(x, y) ? [255, 252, 240] : [44, 46, 50]));
  illum.fill((x, y) => (led(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, illum };
}
export function facade() {
  const s = 512, n = makeNoise(201), img = new Img(s, s), rib = (x) => gridLine(x, s / 16);
  img.fill((x, y, u, v) => {
    const k = 1 + (n.fbm(u, v, 24, 3, 16) - 0.5) * 0.12 + (n.fbm(u, v, 3, 3) - 0.5) * 0.1 - smooth(5, 1, rib(x)) * 0.2 + smooth(5, 1, rib(x + 6)) * 0.08 - smooth(3, 0.5, gridLine(y, s / 2)) * 0.4;
    return shade([140, 147, 156], k);
  });
  return { color: img, height: (x, y) => smooth(6, 1, rib(x)) * -3 - smooth(3, 0.5, gridLine(y, s / 2)) * 3 };
}
export function facadeBase() {
  const w = 1024, h = 304, n = makeNoise(211), img = new Img(w, h), illum = new Img(w, h);
  // 512 units of ground floor: a canopy band, glazing in bays, one pair of entrance doors per tile
  const canopy = (y) => y < 44, bay = (x) => gridLine(x, w / 8) < 5, door = (x, y) => Math.abs(x - w / 2) < 92 && y > 96, doorGap = (x) => Math.abs(x - w / 2) < 3;
  img.fill((x, y, u, v) => {
    if (canopy(y)) return y > 36 ? [28, 30, 34] : shade([58, 62, 70], 1 + (n(u, v, 256, 8) - 0.5) * 0.1);
    if (y > h - 14) return shade([70, 70, 68], 1 + (n(u, v, 256) - 0.5) * 0.2);
    if (bay(x) || Math.abs(y - 96) < 4 || doorGap(x) && door(x, y)) return [36, 38, 42];
    if (door(x, y)) return mix([120, 150, 160], [255, 214, 150], smooth(h, 110, y) * 0.55);
    return mix([54, 78, 92], [150, 176, 190], (1 - v) * 0.7 + (n.fbm(u, v, 3, 3) - 0.5) * 0.25);
  });
  illum.fill((x, y) => { const l = door(x, y) && !doorGap(x) && y < h - 14 ? 150 : 0; return [l, l, l]; });
  return { color: img, illum };
}
// The pitch: v8's mowing squares (one tile = 166.4 units, 2 x 2 squares, colours measured from
// materials/tm/grass20) at four times the resolution: 2048 px per tile = 12 texels per unit,
// drawn blade by blade so it stays sharp right under the camera.
export function pitchGrass() {
  const S = 2048, quad = (x, y) => (x < S / 2 ? 0 : 1) + (y < S / 2 ? 0 : 2), lean = [Math.PI * 0.5, 0, Math.PI, Math.PI * 1.5];
  const blades = bladeField(S, (x, y) => lean[quad(x, y)], 520000, 90210), base = [GRASS_DARK, GRASS_MID, GRASS_MID, GRASS_LIGHT];
  return grassImage(S, blades, (x, y) => base[quad(x, y)], 301);
}
// painted line on the design floors: white paint over blades
export function pitchLine() {
  const S = 256, blades = bladeField(S, () => 1, 9000, 21), img = new Img(S, S);
  img.fill((x, y) => { const k = 0.86 + blades.field[y * S + x] * 0.16; return [236 * k, 238 * k, 232 * k]; });
  return { color: img };
}
// one segment of the LED ring on the upper stand's fascia (the plugin tints it): round LEDs on a dark strip
export function ringLeds() {
  const w = 256, h = 64, img = new Img(w, h);
  img.fill((x, y) => { const r = Math.hypot((x % 8) - 3.5, (y % 8) - 3.5), v = y < 3 || y > h - 4 ? 30 : r < 2.4 ? 255 : r < 3.4 ? 150 : 70; return [v, v, v]; });
  return { color: img };
}
export function glassRail() {
  const s = 64, img = new Img(s, s), alpha = new Img(s, s);
  const rail = (x, y) => y < 8 || gridLine(x, s) < 2;
  img.fill((x, y) => (rail(x, y) ? [176, 180, 184] : [150, 196, 200]));
  alpha.fill((x, y) => (rail(x, y) ? [255, 255, 255] : [52, 52, 52]));
  return { color: img, alpha };
}
export function shutter() {
  const s = 256, n = makeNoise(221), img = new Img(s, s);
  img.fill((x, y, u, v) => {
    if (y > s - 30) return (Math.floor((x + (s - y)) / 22) % 2) ? [236, 190, 24] : [26, 26, 28];
    const slat = (y % 12) / 12;
    return shade([126, 130, 136], 0.84 + slat * 0.26 + (n.fbm(u, v, 5, 3) - 0.5) * 0.1 - (slat < 0.12 ? 0.22 : 0));
  });
  return { color: img, height: (x, y) => ((y % 12) / 12) * 3 };
}
