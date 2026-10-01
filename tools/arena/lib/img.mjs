// Small raster helpers for generated textures: float RGBA image, tileable noise, PNG in/out.
import fs from "node:fs";
import zlib from "node:zlib";

export class Img {
  constructor(w, h) { this.w = w; this.h = h; this.d = new Float32Array(w * h * 4); this.height = null; }
  // fn(x, y, u, v) -> [r, g, b] or [r, g, b, a] (0..255)
  fill(fn) {
    const { w, h, d } = this;
    for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) {
      const c = fn(x, y, (x + 0.5) / w, (y + 0.5) / h), o = (y * w + x) * 4;
      d[o] = c[0]; d[o + 1] = c[1]; d[o + 2] = c[2]; d[o + 3] = c[3] ?? 255;
    }
    return this;
  }
  get(x, y) { const o = ((((y % this.h) + this.h) % this.h) * this.w + (((x % this.w) + this.w) % this.w)) * 4; return [this.d[o], this.d[o + 1], this.d[o + 2], this.d[o + 3]]; }
  // height field for the normal map: fn(x, y, u, v) -> height in texels
  setHeight(fn) { const { w, h } = this; this.height = new Float32Array(w * h); for (let y = 0; y < h; y++) for (let x = 0; x < w; x++) this.height[y * w + x] = fn(x, y, (x + 0.5) / w, (y + 0.5) / h); return this; }
  normalMap(strength = 1) {
    const { w, h, height: H } = this, out = new Img(w, h);
    const at = (x, y) => H[(((y % h) + h) % h) * w + (((x % w) + w) % w)];
    return out.fill((x, y) => {
      const dx = (at(x + 1, y) - at(x - 1, y)) * 0.5 * strength, dy = (at(x, y + 1) - at(x, y - 1)) * 0.5 * strength;
      const l = Math.hypot(dx, dy, 1);
      return [(-dx / l * 0.5 + 0.5) * 255, (-dy / l * 0.5 + 0.5) * 255, (1 / l * 0.5 + 0.5) * 255];
    });
  }
  channel(k) { const out = new Img(this.w, this.h); for (let i = 0; i < this.w * this.h; i++) { const v = this.d[i * 4 + k]; out.d.set([v, v, v, 255], i * 4); } return out; }
  png(file, channels = 3) {
    const { w, h, d } = this, raw = Buffer.alloc((w * channels + 1) * h);
    for (let y = 0; y < h; y++) {
      const row = y * (w * channels + 1); raw[row] = 0;
      for (let x = 0; x < w; x++) for (let c = 0; c < channels; c++) raw[row + 1 + x * channels + c] = Math.max(0, Math.min(255, Math.round(d[(y * w + x) * 4 + (channels === 1 ? 0 : c)])));
    }
    const head = Buffer.alloc(13); head.writeUInt32BE(w, 0); head.writeUInt32BE(h, 4); head[8] = 8; head[9] = channels === 4 ? 6 : channels === 3 ? 2 : 0;
    fs.writeFileSync(file, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", head), chunk("IDAT", zlib.deflateSync(raw, { level: 6 })), chunk("IEND", Buffer.alloc(0))]));
  }
}
const crcTable = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const v of b) c = crcTable[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
function chunk(type, data) { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); }

// 8-bit RGB / RGBA PNG reader (what System.Drawing writes)
export function readPng(file) {
  const b = fs.readFileSync(file); let o = 8, w = 0, h = 0, type = 0; const idat = [];
  while (o < b.length) {
    const len = b.readUInt32BE(o), t = b.toString("latin1", o + 4, o + 8), data = b.subarray(o + 8, o + 8 + len);
    if (t === "IHDR") { w = data.readUInt32BE(0); h = data.readUInt32BE(4); type = data[9]; if (data[8] !== 8 || ![2, 6].includes(type)) throw new Error(`${file}: unsupported PNG (depth ${data[8]}, type ${type})`); }
    if (t === "IDAT") idat.push(data);
    o += 12 + len;
  }
  const bpp = type === 6 ? 4 : 3, raw = zlib.inflateSync(Buffer.concat(idat)), stride = w * bpp, img = new Img(w, h), prev = Buffer.alloc(stride), cur = Buffer.alloc(stride);
  for (let y = 0; y < h; y++) {
    const f = raw[y * (stride + 1)]; raw.copy(cur, 0, y * (stride + 1) + 1, (y + 1) * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const a = x >= bpp ? cur[x - bpp] : 0, up = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
      let v = cur[x];
      if (f === 1) v += a; else if (f === 2) v += up; else if (f === 3) v += (a + up) >> 1;
      else if (f === 4) { const p = a + up - c, pa = Math.abs(p - a), pb = Math.abs(p - up), pc = Math.abs(p - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? up : c; }
      cur[x] = v & 255;
    }
    for (let x = 0; x < w; x++) img.d.set([cur[x * bpp], cur[x * bpp + 1], cur[x * bpp + 2], bpp === 4 ? cur[x * bpp + 3] : 255], (y * w + x) * 4);
    cur.copy(prev);
  }
  return img;
}

// ---- tileable value noise ----------------------------------------------------------------------
export function makeNoise(seed) {
  let s = seed >>> 0 || 1;
  const rnd = () => { s ^= s << 13; s >>>= 0; s ^= s >>> 17; s ^= s << 5; s >>>= 0; return s / 0x100000000; };
  const grids = new Map();
  const grid = (n) => { if (!grids.has(n)) grids.set(n, Float32Array.from({ length: n * n }, rnd)); return grids.get(n); };
  // periodic 2D value noise with nx x ny cells over the unit square
  const noise = (u, v, nx, ny = nx) => {
    const n = Math.max(nx, ny), g = grid(n);
    const x = u * nx, y = v * ny, x0 = Math.floor(x), y0 = Math.floor(y), fx = x - x0, fy = y - y0;
    const at = (i, j) => g[(((j % ny) + ny) % ny) * n + (((i % nx) + nx) % nx)];
    const sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
    const a = at(x0, y0) + (at(x0 + 1, y0) - at(x0, y0)) * sx, b = at(x0, y0 + 1) + (at(x0 + 1, y0 + 1) - at(x0, y0 + 1)) * sx;
    return a + (b - a) * sy;
  };
  noise.fbm = (u, v, base, octaves, stretchY = 1) => {
    let sum = 0, amp = 1, norm = 0, f = base;
    for (let o = 0; o < octaves; o++) { sum += amp * noise(u, v, f, Math.max(1, Math.round(f / stretchY))); norm += amp; amp *= 0.5; f *= 2; }
    return sum / norm;
  };
  noise.rnd = rnd;
  return noise;
}
export const mix = (a, b, t) => a.map((v, k) => v + (b[k] - v) * t);
export const clamp01 = (v) => Math.max(0, Math.min(1, v));
export const smooth = (a, b, x) => { const t = clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); };
// distance to the nearest line of a grid with the given period (in the same units as x)
export const gridLine = (x, period) => { const m = ((x % period) + period) % period; return Math.min(m, period - m); };
// signed distance to a rounded rectangle centred at (cx, cy)
export const roundRect = (x, y, cx, cy, hw, hh, r) => { const qx = Math.abs(x - cx) - hw + r, qy = Math.abs(y - cy) - hh + r; return Math.min(Math.max(qx, qy), 0) + Math.hypot(Math.max(qx, 0), Math.max(qy, 0)) - r; };
