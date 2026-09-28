#!/usr/bin/env node
// Radar / minimap of soccer_cssl_stadium_v8 (owner 2026-09-28: "update the
// minimap with the latest map and make sure all the map is included").
//
// The old image (1024 px, 3.35 u/px) covered x/y +-1715, so the stands and the
// tunnels were cut off. Same zoom (3.349609375 units per pixel, so the radar
// window shows the same area as before) on a 2048 px canvas: +-3430 units,
// which holds the whole stadium (world meshes measured with ValveResourceFormat:
// x +-2344, y +-2728).
//
// World -> pixel: px = (x + 3430) / SCALE, py = (3430 - y) / SCALE. +y is up in
// the image and red's goal (the +y goal), as in the old radar. The matching
// overview file is src/workshop-addon/soccermod_stadium_radar/resource/overviews/
// soccer_cssl_stadium_v8.txt (pos_x -3430, pos_y 3430, scale 3.349609375).
//
// usage: node tools/radar/generate-radar.mjs <out.png> [--preview <dir>]
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";

const out = process.argv[2];
if (!out) { console.error("usage: generate-radar.mjs <out.png>"); process.exit(1); }
const N = 2048, SCALE = 6860 / N, ORIGIN = 3430;
const wx = (i) => -ORIGIN + (i + 0.5) * SCALE, wy = (j) => ORIGIN - (j + 0.5) * SCALE;

// ---- the map (measured; see tools/grass/generate-shell-grass.mjs and generate-kickoff-curtain.mjs) --
const HX = 1280, HY = 1664;                 // pitch (grass)
const LINE = [236, 238, 232];
const rects = [
  [1018, -1384, 1024, 1384], [-1024, -1384, -1018, 1384], [-1024, 1378, 1024, 1384], [-1024, -1384, 1024, -1378], [-1018, -3, -11, 3], [11, -3, 1018, 3],
  [598, 832, 604, 1378], [-608, 832, -602, 1378], [-602, 832, 598, 838], [598, -1378, 604, -832], [-608, -1378, -602, -832], [-602, -838, 598, -832],
  [314, 1184, 320, 1378], [-320, 1184, -314, 1378], [-314, 1184, 314, 1190], [314, -1378, 320, -1184], [-320, -1378, -314, -1184], [-314, -1190, 314, -1184],
];
const half = Math.acos(128 / 258);
const rings = [
  [0, 0, 250, 256, 0, Math.PI * 2],
  [0, 960, 252, 258, -Math.PI / 2 - half, -Math.PI / 2 + half], [0, -960, 252, 258, Math.PI / 2 - half, Math.PI / 2 + half],
  [-1024, 1384, 48, 54, -Math.PI / 2, 0], [1024, 1384, 48, 54, Math.PI, Math.PI * 1.5], [-1024, -1384, 48, 54, 0, Math.PI / 2], [1024, -1384, 48, 54, Math.PI / 2, Math.PI],
];
const discs = [[0, 0, 11], [0, 1016, 11], [0, -1016, 11]];
const tau = Math.PI * 2, nrm = (v) => ((v % tau) + tau) % tau;
const angleIn = (t, a0, a1) => { if (a1 - a0 >= tau - 1e-9) return true; const s = nrm(a0), e = nrm(a1), x = nrm(t); return s <= e ? x >= s && x <= e : x >= s || x <= e; };
const onLine = (x, y) => {
  for (const [x0, y0, x1, y1] of rects) if (x >= x0 && x <= x1 && y >= y0 && y <= y1) return true;
  for (const [cx, cy, r0, r1, a0, a1] of rings) { const d = Math.hypot(x - cx, y - cy); if (d >= r0 && d <= r1 && angleIn(Math.atan2(y - cy, x - cx), a0, a1)) return true; }
  for (const [cx, cy, r] of discs) if (Math.hypot(x - cx, y - cy) <= r) return true;
  return false;
};
const inRect = (x, y, x0, y0, x1, y1) => x >= x0 && x <= x1 && y >= y0 && y <= y1;

// perimeter wall (4 units either side of its centre line; gap |y| < 129 on the long sides)
const WALL_R = 6;
const wall = (x, y) =>
  (Math.abs(Math.abs(x) - 1282) <= WALL_R && Math.abs(y) >= 129 && Math.abs(y) <= 1666 + WALL_R) ||
  (Math.abs(Math.abs(y) - 1666) <= WALL_R && Math.abs(x) <= 1286);
// tunnel to the door wall at |x| 1580 through the wall gap; its floor is at pitch height
const tunnel = (x, y) => Math.abs(y) < 129 && Math.abs(x) > 1282 && Math.abs(x) <= 1580;
const doorWall = (x, y) => Math.abs(y) < 129 && Math.abs(Math.abs(x) - 1584) <= 6;
// dugouts beside the doors (behind the wall, both sides of each tunnel)
const dugout = (x, y) => Math.abs(x) >= 1440 && Math.abs(x) <= 1575 && Math.abs(y) >= 200 && Math.abs(y) <= 470;
// goal nets (frame line y 1384, back 83 behind it) and the frame tubes
const goalNet = (x, y) => Math.abs(x) <= 128 && Math.abs(y) >= 1384 && Math.abs(y) <= 1467;
const goalFrame = (x, y) => goalNet(x, y) && (Math.abs(Math.abs(x) - 128) <= 3.5 || Math.abs(Math.abs(y) - 1384) <= 3.5 || Math.abs(Math.abs(y) - 1467) <= 3.5);
// stadium footprint: pitch surround, stands, walkways (the world meshes end at these)
const stadium = (x, y) => Math.abs(x) <= 2344 && Math.abs(y) <= 2728;
const concourse = (x, y) => Math.abs(x) <= 1584 && Math.abs(y) <= 1968;           // floor around the pitch behind the wall
const stands = (x, y) => stadium(x, y) && !concourse(x, y);

// ---- colours (sRGB 0-255) -------------------------------------------------------------
const GRASS_A = [52, 84, 30], GRASS_B = [60, 96, 35], WALL_C = [22, 24, 27];
function colourAt(x, y) {
  if (onLine(x, y) && Math.abs(x) <= HX && Math.abs(y) <= HY) return LINE;
  if (goalFrame(x, y)) return [245, 245, 245];
  if (goalNet(x, y)) return y > 0 ? [214, 40, 46] : [36, 92, 220];
  if (wall(x, y)) return WALL_C;
  if (doorWall(x, y)) return [86, 74, 66];
  if (dugout(x, y)) return (Math.abs(Math.abs(x) - 1440) < 6 || Math.abs(Math.abs(x) - 1575) < 6 || Math.abs(Math.abs(y) - 200) < 6 || Math.abs(Math.abs(y) - 470) < 6) ? [92, 98, 104] : [38, 42, 46];
  if (Math.abs(x) <= HX && Math.abs(y) <= HY) {
    // the map's own mowing squares (two crossing sets of 83.2 unit bands)
    const k = (Math.floor((x + 4000) / 83.2) + Math.floor((y + 4000) / 83.2)) & 1;
    return k ? GRASS_B : GRASS_A;
  }
  if (tunnel(x, y)) return [74, 78, 82];
  if (concourse(x, y)) {
    // floor between the wall and the stands: dark with a faint tile grid
    const g = (Math.abs(x) % 96 < 3 || Math.abs(y) % 96 < 3) ? 34 : 29;
    return [g, g + 2, g + 5];
  }
  if (stands(x, y)) {
    // seating bowl: rows running along the pitch, lighter rows every 128 units, aisles every ~700
    const ring = Math.max(Math.abs(x) / 2344, Math.abs(y) / 2728);
    const row = (Math.max(Math.abs(x) - 1584, Math.abs(y) - 1968) % 128 + 128) % 128;
    const aisle = (Math.abs(x) % 700 < 20 && Math.abs(y) > 1968) || (Math.abs(y) % 700 < 20 && Math.abs(x) > 1584);
    const base = 30 + Math.round(10 * ring);
    return aisle ? [base + 22, base + 24, base + 27] : row < 14 ? [base + 12, base + 14, base + 17] : [base, base + 2, base + 5];
  }
  return [12, 13, 15];
}

// ---- render (2 x 2 supersampling) -------------------------------------------------------
const raw = Buffer.alloc((N * 3 + 1) * N);
const off = [0.25, 0.75];
for (let j = 0; j < N; j++) {
  raw[j * (N * 3 + 1)] = 0;
  for (let i = 0; i < N; i++) {
    let r = 0, g = 0, b = 0;
    for (const dy of off) for (const dx of off) {
      const x = -ORIGIN + (i + dx) * SCALE, y = ORIGIN - (j + dy) * SCALE;
      const c = colourAt(x, y); r += c[0]; g += c[1]; b += c[2];
    }
    const o = j * (N * 3 + 1) + 1 + i * 3;
    raw[o] = Math.round(r / 4); raw[o + 1] = Math.round(g / 4); raw[o + 2] = Math.round(b / 4);
  }
}
const table = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const v of b) c = table[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(N, 0); ihdr.writeUInt32BE(N, 4); ihdr[8] = 8; ihdr[9] = 2;
fs.mkdirSync(path.dirname(out), { recursive: true });
fs.writeFileSync(out, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw, { level: 9 })), chunk("IEND", Buffer.alloc(0))]));
console.log(`radar ${N}x${N}, ${SCALE} u/px, covers +-${ORIGIN} -> ${out} (${fs.statSync(out).size} B)`);
