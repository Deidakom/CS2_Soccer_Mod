#!/usr/bin/env node
// Own woven-fabric textures for the waving banners (red/blue, same tone as
// the v8 map banners), plus their materials. usage: generate-banner-fabric.mjs <addon content dir>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";

const out = process.argv[2];
const N = 256;
const crcT = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const v of b) c = crcT[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
function png(file, px) {
  const raw = Buffer.alloc((N * 3 + 1) * N);
  for (let y = 0; y < N; y++) { raw[y * (N * 3 + 1)] = 0; for (let x = 0; x < N; x++) for (let c = 0; c < 3; c++) raw[y * (N * 3 + 1) + 1 + x * 3 + c] = px(x, y, c); }
  const ih = Buffer.alloc(13); ih.writeUInt32BE(N, 0); ih.writeUInt32BE(N, 4); ih[8] = 8; ih[9] = 2;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ih), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]));
}
let seed = 7; const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);
const noise = Array.from({ length: N * N }, () => rnd());
const weave = (x, y) => { const cell = 4, u = x % cell, v = y % cell, warp = ((Math.floor(x / cell) + Math.floor(y / cell)) & 1); const thread = warp ? Math.sin(Math.PI * (u + 0.5) / cell) : Math.sin(Math.PI * (v + 0.5) / cell); return 0.86 + 0.14 * thread; };
for (const [name, base] of [["red", [168, 38, 36]], ["blue", [34, 56, 132]]]) {
  const dir = path.join(out, "materials/soccermod/atmo");
  png(path.join(dir, `banner_${name}_color.png`), (x, y, c) => Math.max(0, Math.min(255, Math.round(base[c] * weave(x, y) * (0.95 + 0.1 * noise[y * N + x])))));
  fs.writeFileSync(path.join(dir, `banner_${name}.vmat`), `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flMetalness"\t"0.000"
\t"TextureColor"\t"materials/soccermod/atmo/banner_${name}_color.png"
\t"TextureRoughness"\t"[0.900000 0.900000 0.900000 0.000000]"
}
`);
}
console.log("banner fabric red/blue ->", out);
