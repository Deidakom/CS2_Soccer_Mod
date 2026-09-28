#!/usr/bin/env node
// Goal net (owner 2026-09-28): a knotted rope net with hexagonal meshes that
// is really see-through between the ropes (the old tinted version looked like
// a wall). Replaces the map's own material materials/soccer/goalnetting.vmat
// (soccer_cssl_stadium_v8, a 128 px tile with one square mesh), so the net
// geometry stays; one tile now holds a patch of a honeycomb, scaled up in the
// material (see the vmat below).
// Everything is procedural (our own asset): three-strand twisted ropes along
// the hexagon edges, bulky knots at the corners. Outputs colour (rope shading
// baked in), translucency (alpha test), normal and roughness maps.
//
// usage: node tools/net/generate-goal-net.mjs <csgo_addons/soccermod_menu> [--preview <dir>]
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";

const out = process.argv[2];
if (!out) { console.error("usage: generate-goal-net.mjs <addon content dir> [--preview <dir>]"); process.exit(1); }
const previewDir = process.argv.includes("--preview") ? process.argv[process.argv.indexOf("--preview") + 1] : null;

const N = 1024;              // tile size in pixels (owner 2026-09-28: 2x sharper)
const R = 0.011;             // rope radius (tile units)
const RK = 0.022;            // knot radius
const TWIST = 24;            // strand turns along one tile length
const MAT = "materials/soccer/goalnetting";
const TEXP = "materials/soccer/goalnetting_rope";

// Honeycomb, period 1 x 1: two pointy-top hexagons per tile side by side
// (width 1/2, height 2/3 - a little taller than regular, like a goal net),
// centred at (1/4, 1/2) and (3/4, 1/2); the offset row sits between them.
// 2026-09-28 owner: smaller holes - COLS hexagons across and ROWS row pairs
// down per tile (was 2 x 1).
const COLS = 3, ROWS = 2, W = 1 / COLS, P = 1 / ROWS;
const segs = [], knots = [];
for (let r = 0; r < ROWS; r++) for (let c = 0; c < COLS; c++) {
  const cx = W / 2 + c * W, cy = P / 2 + r * P, hw = W / 2;
  const top = [cx, cy - P / 3], ur = [cx + hw, cy - P / 6], lr = [cx + hw, cy + P / 6], bot = [cx, cy + P / 3], ll = [cx - hw, cy + P / 6], ul = [cx - hw, cy - P / 6];
  segs.push([top, ur], [ur, lr], [lr, bot], [bot, ll], [ll, ul], [ul, top]);
  segs.push([top, [cx, cy - 2 * P / 3]]); // vertical edge up to the offset row (wraps)
  knots.push(top, ur, lr, bot);
}

function png(w, h, ch, pixel) {
  const raw = Buffer.alloc((w * ch + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * ch + 1)] = 0; for (let x = 0; x < w; x++) pixel(x, y, raw, y * (w * ch + 1) + 1 + x * ch); }
  const table = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (b) => { let c = 0xffffffff; for (const v of b) c = table[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = ch === 1 ? 0 : ch === 3 ? 2 : 6;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw, { level: 9 })), chunk("IEND", Buffer.alloc(0))]);
}
const clamp = (v) => Math.max(0, Math.min(255, Math.round(v)));

// Per pixel: height (0 = hole), rope shading term, alpha coverage.
const H = new Float32Array(N * N), SH = new Float32Array(N * N), A = new Float32Array(N * N);
const SS = 3; // supersampling for the alpha edge
function sample(u, v) {
  let best = { d: 1e9, h: 0, s: 0 };
  for (let oy = -1; oy <= 1; oy++) for (let ox = -1; ox <= 1; ox++) {
    const px = u + ox, py = v + oy;
    for (const [[x0, y0], [x1, y1]] of segs) {
      const dx = x1 - x0, dy = y1 - y0, L2 = dx * dx + dy * dy;
      const t = Math.max(0, Math.min(1, ((px - x0) * dx + (py - y0) * dy) / L2));
      const qx = x0 + dx * t, qy = y0 + dy * t, d = Math.hypot(px - qx, py - qy);
      if (d < R && d < best.d) {
        const L = Math.sqrt(L2), side = ((px - x0) * dy - (py - y0) * dx) / L / R; // -1..1 across
        const hh = Math.sqrt(1 - (d / R) ** 2);
        // three twisted strands: diagonal grooves along the rope
        const strand = Math.cos((t * L * TWIST + side * 0.55) * Math.PI * 2 * 1.5);
        best = { d, h: hh * (0.82 + 0.18 * strand), s: 0.72 + 0.28 * strand };
      }
    }
    for (const [kx, ky] of knots) {
      const d = Math.hypot(px - kx, py - ky);
      // Knot: a smooth round thickening the ropes run into (owner: the
      // swirl pattern at the mesh corners looked weird), with a soft
      // rim so it reads as a tied bundle.
      if (d < RK) {
        const r = d / RK, hh = Math.sqrt(1 - r * r) * 1.25;
        const rim = 1 - 0.18 * Math.exp(-((r - 0.72) ** 2) / 0.012);
        if (hh > best.h) best = { d, h: hh, s: (0.78 + 0.12 * (1 - r)) * rim };
      }
    }
  }
  return best;
}
for (let y = 0; y < N; y++) for (let x = 0; x < N; x++) {
  let cover = 0;
  for (let sy = 0; sy < SS; sy++) for (let sx = 0; sx < SS; sx++) if (sample((x + (sx + 0.5) / SS) / N, (y + (sy + 0.5) / SS) / N).h > 0) cover++;
  const c = sample((x + 0.5) / N, (y + 0.5) / N), i = y * N + x;
  H[i] = c.h; SH[i] = c.s; A[i] = cover / (SS * SS);
}

const at = (arr, x, y) => arr[(((y % N) + N) % N) * N + (((x % N) + N) % N)];
const color = png(N, N, 3, (x, y, b, o) => {
  const h = at(H, x, y), s = at(SH, x, y), i = y * N + x;
  const shade = h > 0 ? (0.52 + 0.48 * h) * s : 0.7; // holes: neutral (never shown)
  const dirt = 1 - 0.06 * Math.sin(x * 0.07) * Math.sin(y * 0.05);
  b[o] = clamp(232 * shade * dirt); b[o + 1] = clamp(228 * shade * dirt); b[o + 2] = clamp(214 * shade * dirt);
});
const trans = png(N, N, 1, (x, y, b, o) => { b[o] = clamp(at(A, x, y) * 255); });
const normal = png(N, N, 3, (x, y, b, o) => {
  const k = 6.0, nx = (at(H, x - 1, y) - at(H, x + 1, y)) * k, ny = (at(H, x, y - 1) - at(H, x, y + 1)) * k, len = Math.hypot(nx, ny, 1);
  b[o] = clamp((nx / len * 0.5 + 0.5) * 255); b[o + 1] = clamp((ny / len * 0.5 + 0.5) * 255); b[o + 2] = clamp((1 / len * 0.5 + 0.5) * 255);
});
const rough = png(N, N, 1, (x, y, b, o) => { b[o] = clamp((0.78 + 0.12 * (1 - at(SH, x, y))) * 255); });

// csgo_complex, translucent and lit with the rope normals: the holes are
// fully clear up close, and far away the mips fade the ropes into a light
// haze (alpha test made the whole net vanish at a distance). The map's net is
// made of closed 2-unit slabs (func_brush), so no backfaces are needed.
// Measured from the map's net brush UVs: one texture repeat covers only 2.56
// units there; g_vTexCoordScale 0.16 stretches it to one repeat per 16 units,
// the mesh size the owner picked in the preview.
const vmat = `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_TRANSLUCENT"\t"1"
\t"g_flMetalness"\t"0.000"
\t"g_vTexCoordScale"\t"[0.160 0.160]"
\t"TextureColor"\t"${TEXP}_color.png"
\t"TextureTranslucency"\t"${TEXP}_trans.png"
\t"TextureNormal"\t"${TEXP}_normal.png"
\t"TextureRoughness"\t"${TEXP}_rough.png"
\t"SystemAttributes"
\t{
\t\t"PhysicsSurfaceProperties"\t"glass"
\t}
}
`;

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(`${TEXP}_color.png`, color);
write(`${TEXP}_trans.png`, trans);
write(`${TEXP}_normal.png`, normal);
write(`${TEXP}_rough.png`, rough);
write(`${MAT}.vmat`, vmat);
if (previewDir) {
  // one tile as RGBA (colour + alpha) for the browser preview page
  fs.writeFileSync(path.join(previewDir, "net_rgba.png"), png(N, N, 4, (x, y, b, o) => {
    const h = at(H, x, y), s = at(SH, x, y), shade = (0.52 + 0.48 * h) * s;
    b[o] = clamp(232 * shade); b[o + 1] = clamp(228 * shade); b[o + 2] = clamp(214 * shade); b[o + 3] = clamp(at(A, x, y) * 255);
  }));
  // 3 x 3 tiles over a sky blue, alpha-composited, for a quick look.
  const T = 3, W = N * T;
  fs.writeFileSync(path.join(previewDir, "net_preview.png"), png(W, W, 3, (x, y, b, o) => {
    const tx = x % N, ty = y % N, a = at(A, tx, ty), h = at(H, tx, ty), s = at(SH, tx, ty);
    const shade = (0.52 + 0.48 * h) * s, sky = [150, 190, 230];
    const rope = [232 * shade, 228 * shade, 214 * shade];
    for (let k = 0; k < 3; k++) b[o + k] = clamp(sky[k] * (1 - a) + rope[k] * a);
  }));
}
console.log(`goal net: ${N}x${N} tile, rope radius ${R}, knots ${RK} -> ${out}`);
