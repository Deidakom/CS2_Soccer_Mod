#!/usr/bin/env node
// Ball skins (owner 2026-10-18): the map ball's own model (models/ball/jabulani_edit,
// same mesh and UVs, so exactly the same size) with other textures, drawn as a shell
// round the match ball by the plugin (SoccerModMvpPlugin.BallSkins.cs).
//
//   models/soccermod/ball/wc_ball.vmdl - the map ball's render mesh, material groups
//   skin 0..4 = Trionda 2026, Al Rihla 2022, Telstar 18, Jabulani 2010 (the map
//   ball's own texture), SoccerMod (own design).
//
// Textures: every texel of the ball's UV layout is turned into its direction on the
// ball (barycentric on the mesh) and coloured from a product photo of the ball
// (ChatGPT, owner's account): the photo shows the ball twice, front and back, and
// each half is projected orthographically onto its hemisphere, blended at the rim.
//
// usage: node tools/ball/generate-ball-skins.mjs <csgo_addons/soccermod_ballskins>
//          <map ball dmx> <map ball colour png> <reference dir> [--preview <png>]
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import zlib from "node:zlib";
import { execFileSync } from "node:child_process";
import { readMesh } from "./dmx-binary.mjs";

const [out, meshFile, ballTexture, refDir] = process.argv.slice(2);
const previewAt = process.argv.indexOf("--preview");
const preview = previewAt > 0 ? process.argv[previewAt + 1] : null;
if (!refDir) { console.error("usage: generate-ball-skins.mjs <addon content dir> <ball dmx> <ball colour png> <reference dir> [--preview <png>]"); process.exit(1); }

const MODEL = "models/soccermod/ball/wc_ball", MAT_DIR = "materials/soccermod/ball", ORIGINAL_MAT = "materials/models/jabulani/jabulani.vmat";
const SIZE = 2048;
const SKINS = [
  { key: "trionda", ref: "full-1-trionda.png" },
  { key: "alrihla", ref: "full-2-alrihla.png" },
  { key: "telstar18", ref: "full-3-telstar18.jpg" },
  { key: "jabulani", ref: null },                      // the map ball's own texture
  { key: "soccermod", ref: "full-5-soccermod.jpg" },
];

// ---- images: anything -> 24-bit BMP via System.Drawing, then raw RGB ---------------
function loadImage(file) {
  const tmp = path.join(os.tmpdir(), `ballskin-${process.pid}-${path.basename(file)}.bmp`);
  execFileSync("powershell", ["-NoProfile", "-Command",
    `Add-Type -AssemblyName System.Drawing; $s=[System.Drawing.Image]::FromFile('${file}'); $b=New-Object System.Drawing.Bitmap($s.Width,$s.Height,[System.Drawing.Imaging.PixelFormat]::Format24bppRgb); $g=[System.Drawing.Graphics]::FromImage($b); $g.DrawImage($s,0,0,$s.Width,$s.Height); $b.Save('${tmp}',[System.Drawing.Imaging.ImageFormat]::Bmp)`]);
  const b = fs.readFileSync(tmp); fs.unlinkSync(tmp);
  const off = b.readUInt32LE(10), w = b.readInt32LE(18), h0 = b.readInt32LE(22), h = Math.abs(h0), stride = Math.ceil(w * 3 / 4) * 4;
  const rgb = new Float32Array(w * h * 3);
  for (let y = 0; y < h; y++) {
    const row = off + (h0 > 0 ? h - 1 - y : y) * stride;
    for (let x = 0; x < w; x++) { const o = (y * w + x) * 3; rgb[o] = b[row + x * 3 + 2] / 255; rgb[o + 1] = b[row + x * 3 + 1] / 255; rgb[o + 2] = b[row + x * 3] / 255; }
  }
  return { w, h, rgb };
}
const px = (img, x, y) => { x = Math.max(0, Math.min(img.w - 1, x)); y = Math.max(0, Math.min(img.h - 1, y)); const o = (y * img.w + x) * 3; return [img.rgb[o], img.rgb[o + 1], img.rgb[o + 2]]; };
function bilinear(img, x, y) {
  const x0 = Math.floor(x), y0 = Math.floor(y), fx = x - x0, fy = y - y0;
  const a = px(img, x0, y0), b = px(img, x0 + 1, y0), c = px(img, x0, y0 + 1), d = px(img, x0 + 1, y0 + 1);
  return [0, 1, 2].map((k) => (a[k] * (1 - fx) + b[k] * fx) * (1 - fy) + (c[k] * (1 - fx) + d[k] * fx) * fy);
}

// The two balls in a reference photo: the non-background extent of each half.
function findBalls(img) {
  const bg = px(img, 2, 2);
  const balls = [];
  for (const [x0, x1] of [[0, img.w >> 1], [img.w >> 1, img.w]]) {
    let minX = 1e9, maxX = -1, minY = 1e9, maxY = -1;
    for (let y = 0; y < img.h; y += 1) for (let x = x0; x < x1; x += 1) {
      const c = px(img, x, y);
      if (Math.abs(c[0] - bg[0]) + Math.abs(c[1] - bg[1]) + Math.abs(c[2] - bg[2]) > 0.12) {
        minX = Math.min(minX, x); maxX = Math.max(maxX, x); minY = Math.min(minY, y); maxY = Math.max(maxY, y);
      }
    }
    balls.push({ cx: (minX + maxX) / 2, cy: (minY + maxY) / 2, r: Math.min(maxX - minX, maxY - minY) / 2 * 0.985 });
  }
  return balls;
}

// Direction (ball frame: +x towards the front camera, +y left, +z up) -> photo colour.
function projector(img) {
  const [front, back] = findBalls(img);
  // Near the photo's rim the projection smears (one pixel covers a wide band of the
  // ball), so below RIM_FACING the pattern is mirrored back from just inside instead.
  const RIM_FACING = 0.22;
  const sample = (ball, right, up, facing) => {
    if (facing < RIM_FACING) {
      const mirrored = Math.min(1, 2 * RIM_FACING - facing), rr = Math.hypot(right, up) || 1, k = Math.sqrt(1 - mirrored * mirrored) / rr;
      right *= k; up *= k; facing = mirrored;
    }
    const c = bilinear(img, ball.cx + right * ball.r, ball.cy - up * ball.r);
    const gain = 1 / (0.78 + 0.22 * Math.max(0, facing));   // the photo's rim falloff
    return c.map((v) => Math.min(1, v * gain));
  };
  return (d) => {
    const f = sample(front, d[1], d[2], d[0]);                // front camera on +x: screen right = +y
    const b = sample(back, -d[1], d[2], -d[0]);               // back camera on -x: screen right = -y
    const t = Math.max(0, Math.min(1, (d[0] + 0.15) / 0.3));
    const w = t * t * (3 - 2 * t);
    return [0, 1, 2].map((k) => f[k] * w + b[k] * (1 - w));
  };
}

// ---- the map ball's mesh: per-corner position + uv -------------------------------
const mesh = readMesh(meshFile);
const tris = [];
for (const face of mesh.faces) for (let k = 1; k + 1 < face.length; k++) tris.push([face[0], face[k], face[k + 1]]);
const P = (corner) => mesh.positions[mesh.positionIndices[corner]];
const UV = (corner) => mesh.uvs[mesh.uvIndices[corner]];
const norm = (v) => { const l = Math.hypot(v[0], v[1], v[2]); return [v[0] / l, v[1] / l, v[2] / l]; };

// Texel -> direction map of the UV layout (filled triangles, then a few dilation passes).
function directionMap() {
  const dirs = new Float32Array(SIZE * SIZE * 3), hit = new Uint8Array(SIZE * SIZE);
  for (const t of tris) {
    const uv = t.map((c) => { const u = UV(c); return [u[0] * SIZE, (1 - u[1]) * SIZE]; });
    const pos = t.map(P);
    const minX = Math.max(0, Math.floor(Math.min(...uv.map((q) => q[0])))), maxX = Math.min(SIZE - 1, Math.ceil(Math.max(...uv.map((q) => q[0]))));
    const minY = Math.max(0, Math.floor(Math.min(...uv.map((q) => q[1])))), maxY = Math.min(SIZE - 1, Math.ceil(Math.max(...uv.map((q) => q[1]))));
    const [a, b, c] = uv, den = (b[1] - c[1]) * (a[0] - c[0]) + (c[0] - b[0]) * (a[1] - c[1]);
    if (Math.abs(den) < 1e-9) continue;
    for (let y = minY; y <= maxY; y++) for (let x = minX; x <= maxX; x++) {
      const qx = x + 0.5, qy = y + 0.5;
      const l0 = ((b[1] - c[1]) * (qx - c[0]) + (c[0] - b[0]) * (qy - c[1])) / den;
      const l1 = ((c[1] - a[1]) * (qx - c[0]) + (a[0] - c[0]) * (qy - c[1])) / den, l2 = 1 - l0 - l1;
      if (l0 < -1e-4 || l1 < -1e-4 || l2 < -1e-4) continue;
      const o = (y * SIZE + x) * 3, d = norm([0, 1, 2].map((k) => pos[0][k] * l0 + pos[1][k] * l1 + pos[2][k] * l2));
      dirs[o] = d[0]; dirs[o + 1] = d[1]; dirs[o + 2] = d[2]; hit[y * SIZE + x] = 1;
    }
  }
  for (let pass = 0; pass < 6; pass++) {               // bleed past the island edges (mip safety)
    const next = hit.slice();
    for (let y = 0; y < SIZE; y++) for (let x = 0; x < SIZE; x++) {
      if (hit[y * SIZE + x]) continue;
      for (const [dx, dy] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
        const nx = x + dx, ny = y + dy;
        if (nx < 0 || ny < 0 || nx >= SIZE || ny >= SIZE || !hit[ny * SIZE + nx]) continue;
        const o = (y * SIZE + x) * 3, n = (ny * SIZE + nx) * 3;
        dirs[o] = dirs[n]; dirs[o + 1] = dirs[n + 1]; dirs[o + 2] = dirs[n + 2]; next[y * SIZE + x] = 1; break;
      }
    }
    hit.set(next);
  }
  return { dirs, hit };
}

// ---- PNG writer ------------------------------------------------------------------
function png(w, h, rgb) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; rgb.copy(raw, y * (w * 3 + 1) + 1, y * w * 3, (y + 1) * w * 3); }
  const table = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (b) => { let c = 0xffffffff; for (const v of b) c = table[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw, { level: 9 })), chunk("IEND", Buffer.alloc(0))]);
}
const c255 = (v) => Math.max(0, Math.min(255, Math.round(v * 255)));

// ---- the textures ------------------------------------------------------------------
const { dirs, hit } = directionMap();
const original = loadImage(ballTexture);
const textures = SKINS.map((skin) => {
  const buf = Buffer.alloc(SIZE * SIZE * 3, 255);
  if (!skin.ref) {                                     // the map ball's texture, resampled
    for (let y = 0; y < SIZE; y++) for (let x = 0; x < SIZE; x++) {
      const c = bilinear(original, (x + 0.5) / SIZE * original.w - 0.5, (y + 0.5) / SIZE * original.h - 0.5), o = (y * SIZE + x) * 3;
      buf[o] = c255(c[0]); buf[o + 1] = c255(c[1]); buf[o + 2] = c255(c[2]);
    }
    return buf;
  }
  const project = projector(loadImage(path.join(refDir, skin.ref)));
  for (let i = 0; i < SIZE * SIZE; i++) {
    if (!hit[i]) continue;
    const c = project([dirs[i * 3], dirs[i * 3 + 1], dirs[i * 3 + 2]]);
    buf[i * 3] = c255(c[0]); buf[i * 3 + 1] = c255(c[1]); buf[i * 3 + 2] = c255(c[2]);
  }
  return buf;
});

// ---- model: the map ball's dmx with its material renamed, vmat per skin ---------------
function renamedDmx(file, from, to) {
  const b = fs.readFileSync(file), i = b.indexOf(Buffer.from(from + "\0", "latin1"));
  if (i < 0) throw new Error(`material ${from} not in ${file}`);
  return Buffer.concat([b.subarray(0, i), Buffer.from(to, "latin1"), b.subarray(i + from.length)]);
}
const vmat = (tex) => `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"g_flMetalness"	"0.000"
	"g_vColorTint"	"[1.000000 1.000000 1.000000 0.000000]"
	"TextureColor"	"${tex}"
	"TextureRoughness"	"[0.550000 0.550000 0.550000 0.000000]"
}
`;
const mats = SKINS.map((s) => `${MAT_DIR}/wc_${s.key}`);
const vmdl = `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "MaterialGroupList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "DefaultMaterialGroup"
\t\t\t\t\t\tremaps = [  ]
\t\t\t\t\t\tuse_global_default = false
\t\t\t\t\t\tglobal_default_material = ""
\t\t\t\t\t},
${mats.slice(1).map((m, i) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "MaterialGroup"
\t\t\t\t\t\tname = "${SKINS[i + 1].key}"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${mats[0]}.vmat"
\t\t\t\t\t\t\t\tto = "${m}.vmat"
\t\t\t\t\t\t\t},
\t\t\t\t\t\t]
\t\t\t\t\t},
`).join("")}\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "wc_ball"
\t\t\t\t\t\tfilename = "${MODEL}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;

const write = (rel, data) => { const f = path.join(out, rel); fs.mkdirSync(path.dirname(f), { recursive: true }); fs.writeFileSync(f, data); };
SKINS.forEach((s, i) => { write(`${mats[i]}_color.png`, png(SIZE, SIZE, textures[i])); write(`${mats[i]}.vmat`, vmat(`${mats[i]}_color.png`)); });
write(`${MODEL}.dmx`, renamedDmx(meshFile, ORIGINAL_MAT, `${mats[0]}.vmat`));
write(`${MODEL}.vmdl`, vmdl);

// ---- preview: the textured mesh, z-buffered, 4 views per skin -------------------------
if (preview) {
  const V = 300, views = [[0, 0], [90, 0], [180, 0], [0, 75]], W = V * views.length, H = V * SKINS.length;
  const img = Buffer.alloc(W * H * 3, 235), R = Math.max(...mesh.positions.map((p) => Math.hypot(...p)));
  const light = norm([0.5, 0.4, 0.75]);
  textures.forEach((tex, row) => views.forEach(([yaw, pitch], col) => {
    const cy = Math.cos(yaw * Math.PI / 180), sy = Math.sin(yaw * Math.PI / 180), cp = Math.cos(pitch * Math.PI / 180), sp = Math.sin(pitch * Math.PI / 180);
    // camera on +x looking at the ball; screen right = +y, up = +z (after yaw about z, pitch about y)
    const view = (p) => { const x1 = p[0] * cy + p[1] * sy, y1 = -p[0] * sy + p[1] * cy; return [x1 * cp + p[2] * sp, y1, -x1 * sp + p[2] * cp]; };
    const zbuf = new Float32Array(V * V).fill(-1e9);
    for (const t of tris) {
      const q = t.map((c) => { const v = view(P(c)); return [(v[1] / R * 0.92 + 1) * V / 2, (-v[2] / R * 0.92 + 1) * V / 2, v[0]]; });
      const uv = t.map(UV), den = (q[1][1] - q[2][1]) * (q[0][0] - q[2][0]) + (q[2][0] - q[1][0]) * (q[0][1] - q[2][1]);
      if (Math.abs(den) < 1e-9) continue;              // z-buffer keeps the near side
      for (let y = Math.max(0, Math.floor(Math.min(q[0][1], q[1][1], q[2][1]))); y <= Math.min(V - 1, Math.ceil(Math.max(q[0][1], q[1][1], q[2][1]))); y++)
        for (let x = Math.max(0, Math.floor(Math.min(q[0][0], q[1][0], q[2][0]))); x <= Math.min(V - 1, Math.ceil(Math.max(q[0][0], q[1][0], q[2][0]))); x++) {
          const l0 = ((q[1][1] - q[2][1]) * (x + 0.5 - q[2][0]) + (q[2][0] - q[1][0]) * (y + 0.5 - q[2][1])) / den;
          const l1 = ((q[2][1] - q[0][1]) * (x + 0.5 - q[2][0]) + (q[0][0] - q[2][0]) * (y + 0.5 - q[2][1])) / den, l2 = 1 - l0 - l1;
          if (l0 < 0 || l1 < 0 || l2 < 0) continue;
          const z = l0 * q[0][2] + l1 * q[1][2] + l2 * q[2][2];
          if (z <= zbuf[y * V + x]) continue;
          zbuf[y * V + x] = z;
          const u = l0 * uv[0][0] + l1 * uv[1][0] + l2 * uv[2][0], v = l0 * uv[0][1] + l1 * uv[1][1] + l2 * uv[2][1];
          const tx = Math.max(0, Math.min(SIZE - 1, Math.floor(u * SIZE))), ty = Math.max(0, Math.min(SIZE - 1, Math.floor((1 - v) * SIZE))), to = (ty * SIZE + tx) * 3;
          const sx = (x + 0.5) / V * 2 - 1, sy2 = 1 - (y + 0.5) / V * 2, nz = Math.sqrt(Math.max(0, 1 - sx * sx - sy2 * sy2));
          const lam = 0.55 + 0.45 * Math.max(0, sx * light[0] + sy2 * light[1] + nz * light[2]);
          const o = ((row * V + y) * W + col * V + x) * 3;
          img[o] = c255(tex[to] / 255 * lam); img[o + 1] = c255(tex[to + 1] / 255 * lam); img[o + 2] = c255(tex[to + 2] / 255 * lam);
        }
    }
  }));
  fs.writeFileSync(preview, png(W, H, img));
}
console.log(`wrote ${MODEL}.vmdl (${tris.length} triangles, map ball mesh), skins: ${SKINS.map((s) => s.key).join(", ")}`);
