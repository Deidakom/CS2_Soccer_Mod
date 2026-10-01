#!/usr/bin/env node
// SoccerMod Arena: the models the plugin spawns on this map (they travel in the map's own
// Workshop item, so every player has them):
//   models/soccermod_arena/light_ring.vmdl          one LED ring segment, 160 x 40, facing +x
//   models/soccermod_arena/pitch_design_<name>.vmdl the four pitch designs (stripes, lengthwise,
//       diamond, circles) as real geometry: every mowing band is its own polygon with a tiling
//       12-texel-per-unit grass texture, the lines are painted on top. Far sharper than one
//       stretched picture over the whole pitch (3 texels per unit), and only a few megabytes.
// Same origin and height as the plugin's old design floor: model at (0, 0, floor + 0.4).
//
//   node tools/arena/generate-arena-plugin-assets.mjs <content addon dir>
import fs from "node:fs";
import path from "node:path";
import { staticDmx, staticVmdl } from "./lib/dmx.mjs";
import { polyNormal } from "./lib/mesh.mjs";

const addon = process.argv[2];
if (!addon) { console.error("usage: generate-arena-plugin-assets.mjs <content addon dir>"); process.exit(1); }
const DIR = "models/soccermod_arena", M = (n) => `materials/soccermod_arena/${n}.vmat`;
const write = (name, faces) => {
  const d = path.join(addon, DIR); fs.mkdirSync(d, { recursive: true });
  fs.writeFileSync(path.join(d, `${name}.dmx`), staticDmx(name, faces));
  fs.writeFileSync(path.join(d, `${name}.vmdl`), staticVmdl(DIR, name, false));
  console.log(`${DIR}/${name}: ${faces.length} faces`);
};
const face = (material, pts, uvs) => ({ material, pts, uvs, n: polyNormal(pts) });

// ---- LED ring segment ----------------------------------------------------------------------------
export const RING_SEGMENT = { length: 160, height: 40 };
{
  const { length: W, height: H } = RING_SEGMENT;
  write("light_ring", [face(M("light_ring"), [[0, -W / 2, -H / 2], [0, W / 2, -H / 2], [0, W / 2, H / 2], [0, -W / 2, H / 2]], [[0, 1], [1, 1], [1, 0], [0, 0]])]);
}

// ---- pitch designs ---------------------------------------------------------------------------------
const HX = 1280, HY = 1664, TILE = 83.2, LINE_LIFT = 0.12;
const SHADES = [M("pitch_dark"), M("pitch_mid"), M("pitch_light")];
// convex polygon clipped to the pitch rectangle
function clip(poly) {
  let p = poly;
  for (const [axis, sign, lim] of [[0, 1, HX], [0, -1, HX], [1, 1, HY], [1, -1, HY]]) {
    const inside = (q) => sign * q[axis] <= lim + 1e-9, out = [];
    for (let i = 0; i < p.length; i++) {
      const a = p[i], b = p[(i + 1) % p.length], ia = inside(a), ib = inside(b);
      if (ia) out.push(a);
      if (ia !== ib) { const t = (sign * lim - a[axis]) / (b[axis] - a[axis]); out.push([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]); }
    }
    p = out; if (p.length < 3) return null;
  }
  return p;
}
const ccw = (p) => { let a = 0; for (let i = 0; i < p.length; i++) { const q = p[(i + 1) % p.length]; a += p[i][0] * q[1] - q[0] * p[i][1]; } return a < 0 ? [...p].reverse() : p; };
const floor = (material, poly, z = 0, tile = TILE) => { const p = ccw(poly); return face(material, p.map((q) => [q[0], q[1], z]), p.map((q) => [q[0] / tile, -q[1] / tile])); };
const band = (v, w) => Math.floor(v / w) & 1;
const designs = {
  // bands across the pitch, 166.4 wide
  stripes: () => { const out = []; for (let j = 0; j * 166.4 < 2 * HY; j++) { const p = clip([[-HX, -HY + j * 166.4], [HX, -HY + j * 166.4], [HX, -HY + (j + 1) * 166.4], [-HX, -HY + (j + 1) * 166.4]]); if (p) out.push(floor(SHADES[band(j * 166.4 + 1, 166.4) * 2], p)); } return out; },
  // bands along the touchlines, 160 wide
  lengthwise: () => { const out = []; for (let i = 0; i * 160 < 2 * HX; i++) { const p = clip([[-HX + i * 160, -HY], [-HX + (i + 1) * 160, -HY], [-HX + (i + 1) * 160, HY], [-HX + i * 160, HY]]); if (p) out.push(floor(SHADES[band(i * 160 + 1, 160) * 2], p)); } return out; },
  // the map's squares turned 45 degrees: two crossing sets of bands, three shades
  diamond: () => {
    const out = [], w = 166.4, at = (u, v) => [(u + v) / 2 - 4000, (u - v) / 2];
    for (let i = 0; i < 50; i++) for (let j = 0; j < 50; j++) {
      const p = clip([at(i * w, j * w), at((i + 1) * w, j * w), at((i + 1) * w, (j + 1) * w), at(i * w, (j + 1) * w)]);
      if (p) out.push(floor(SHADES[(i & 1) + (j & 1)], p));
    }
    return out;
  },
  // rings round the centre spot, 128 wide
  circles: () => {
    const out = [], N = 144;
    for (let k = 0; k * 128 < Math.hypot(HX, HY); k++) for (let s = 0; s < N; s++) {
      const a0 = (s / N) * 2 * Math.PI, a1 = ((s + 1) / N) * 2 * Math.PI, r0 = k * 128, r1 = (k + 1) * 128;
      const pts = [[r0 * Math.cos(a0), r0 * Math.sin(a0)], [r1 * Math.cos(a0), r1 * Math.sin(a0)], [r1 * Math.cos(a1), r1 * Math.sin(a1)], [r0 * Math.cos(a1), r0 * Math.sin(a1)]];
      const p = clip(k ? pts : pts.slice(1));
      if (p) out.push(floor(SHADES[(k & 1) * 2], p));
    }
    return out;
  },
};
// the painted lines (the plugin's measured v8 markings, tools/pitch/generate-pitch-designs.mjs)
const rects = [
  [1018, -1384, 1024, 1384], [-1024, -1384, -1018, 1384], [-1024, 1378, 1024, 1384], [-1024, -1384, 1024, -1378], [-1018, -3, -11, 3], [11, -3, 1018, 3],
  [598, 832, 604, 1378], [-608, 832, -602, 1378], [-602, 832, 598, 838], [598, -1378, 604, -832], [-608, -1378, -602, -832], [-602, -838, 598, -832],
  [314, 1184, 320, 1378], [-320, 1184, -314, 1378], [-314, 1184, 314, 1190], [314, -1378, 320, -1184], [-320, -1378, -314, -1184], [-314, -1190, 314, -1184],
];
const half = Math.acos(128 / 258);
const rings = [
  [0, 0, 250, 256, 0, Math.PI * 2], [0, 960, 252, 258, -Math.PI / 2 - half, -Math.PI / 2 + half], [0, -960, 252, 258, Math.PI / 2 - half, Math.PI / 2 + half],
  [-1024, 1384, 48, 54, -Math.PI / 2, 0], [1024, 1384, 48, 54, Math.PI, Math.PI * 1.5], [-1024, -1384, 48, 54, 0, Math.PI / 2], [1024, -1384, 48, 54, Math.PI / 2, Math.PI],
];
const discs = [[0, 0, 11], [0, 1016, 11], [0, -1016, 11]];
const lines = [];
for (const [x0, y0, x1, y1] of rects) lines.push(floor(M("pitch_line"), [[x0, y0], [x1, y0], [x1, y1], [x0, y1]], LINE_LIFT, 20.8));
for (const [cx, cy, r0, r1, a0, a1] of rings) {
  const n = Math.max(6, Math.ceil((a1 - a0) / (Math.PI / 48)));
  for (let s = 0; s < n; s++) { const b0 = a0 + (a1 - a0) * s / n, b1 = a0 + (a1 - a0) * (s + 1) / n; lines.push(floor(M("pitch_line"), [[cx + r0 * Math.cos(b0), cy + r0 * Math.sin(b0)], [cx + r1 * Math.cos(b0), cy + r1 * Math.sin(b0)], [cx + r1 * Math.cos(b1), cy + r1 * Math.sin(b1)], [cx + r0 * Math.cos(b1), cy + r0 * Math.sin(b1)]], LINE_LIFT, 20.8)); }
}
for (const [cx, cy, r] of discs) lines.push(floor(M("pitch_line"), Array.from({ length: 16 }, (_, s) => [cx + r * Math.cos(s / 16 * 2 * Math.PI), cy + r * Math.sin(s / 16 * 2 * Math.PI)]), LINE_LIFT, 20.8));
export const PITCH_DESIGNS = Object.keys(designs);
for (const name of PITCH_DESIGNS) write(`pitch_design_${name}`, [...designs[name](), ...lines]);
