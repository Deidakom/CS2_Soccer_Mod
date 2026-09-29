#!/usr/bin/env node
// "Arena Vision" light ring (v8 only): one glowing LED strip segment
// (176 x 28 units, facing +x, origin at its centre) with a white self-lit
// LED texture. The plugin places 80 of them round the roof fascia and colours
// each one with its render colour (chase, flash, sweep, breathing idle).
//
// usage: node tools/atmo/generate-ring.mjs <addon content dir>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import { meshDmx, vmdlText } from "./dmx-lib.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-ring.mjs <addon content dir>"); process.exit(1); }
const MODEL = "models/soccermod/atmo/light_ring";
const MAT = "materials/soccermod/atmo/light_ring.vmat";
const W = 172, H = 14;   // 2026-09-29: flush band on the 16 u fascia front strip, small gaps between segments

// Texture: 3 rows of round LEDs on a dark strip (256 x 16).
const crcT = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
const crc = (b) => { let c = 0xffffffff; for (const v of b) c = crcT[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
const chunk = (t, d) => { const l = Buffer.alloc(4); l.writeUInt32BE(d.length); const td = Buffer.concat([Buffer.from(t), d]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([l, td, c]); };
const TW = 256, TH = 16;
const raw = Buffer.alloc((TW * 3 + 1) * TH);
for (let y = 0; y < TH; y++) for (let x = 0; x < TW; x++) {
  const dx = (x % 4) - 1.5, dy = (y % 4) - 1.5, r = Math.hypot(dx, dy);
  const v = y < 2 || y > 13 ? 30 : r < 1.3 ? 255 : r < 2 ? 150 : 70;
  raw.fill(v, y * (TW * 3 + 1) + 1 + x * 3, y * (TW * 3 + 1) + 4 + x * 3);
}
const ih = Buffer.alloc(13); ih.writeUInt32BE(TW, 0); ih.writeUInt32BE(TH, 4); ih[8] = 8; ih[9] = 2;
const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write("materials/soccermod/atmo/light_ring_color.png", Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ih), chunk("IDAT", zlib.deflateSync(raw)), chunk("IEND", Buffer.alloc(0))]));
write(MAT, `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_SELF_ILLUM"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flMetalness"\t"0.000"
\t"g_flSelfIllumAlbedoFactor"\t"1.000"
\t"g_flSelfIllumBrightness"\t"6.000"
\t"g_flSelfIllumScale"\t"1.000"
\t"g_vSelfIllumTint"\t"[1.000000 1.000000 1.000000 0.000000]"
\t"TextureColor"\t"materials/soccermod/atmo/light_ring_color.png"
\t"TextureRoughness"\t"[0.700000 0.700000 0.700000 0.000000]"
}
`);

const mesh = { name: "light_ring", material: MAT, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
for (const [p, uv] of [[[0, -W / 2, -H / 2], [0, 1]], [[0, -W / 2, H / 2], [0, 0]], [[0, W / 2, H / 2], [1, 0]], [[0, W / 2, -H / 2], [1, 1]]]) {
  mesh.positions.push(p); mesh.normals.push([1, 0, 0]); mesh.uvs.push(uv); mesh.weights.push([1, 0]); mesh.indices.push([0, 0]);
}
mesh.faces.push([0, 1, 2, 3]);
write(`${MODEL}.dmx`, meshDmx("tools/atmo/generate-ring.mjs", [{ name: "root", pos: [0, 0, 0] }], mesh));
let vmdl = vmdlText(MODEL, "light_ring", [], []);
vmdl = vmdl.replace(/\t\t\t\{\n\t\t\t\t_class = "AnimationList"[\s\S]*?\n\t\t\t\},\n/, "");
write(`${MODEL}.vmdl`, vmdl);
console.log(`${MODEL}: ${W}x${H} segment`);
