#!/usr/bin/env node
// Ball skins (owner 2026-10-18): the map ball's own render mesh (same mesh and UVs, so
// exactly the same size) with another texture, drawn as a copy round the match ball by
// the plugin (SoccerModMvpPlugin.BallSkins.cs).
//
//   models/soccermod/ball/wc_ball.vmdl - skin 0 = Trionda 2026.
//
// Texture: every texel of the ball's UV layout is turned into its direction on the ball
// (barycentric on the mesh) and coloured from a turntable image of the ball (12 views
// 30 degrees apart + top + bottom, ChatGPT / owner): each direction takes its best two
// views, blended by how squarely they face it. The turntable's rotation direction, tilt
// and the roll of the top/bottom views are found by fit (the views must agree).
//
// usage: node tools/ball/generate-ball-skins.mjs <csgo_addons/soccermod_ballskins>
//          <map ball dmx> <reference dir> [--preview <png>]
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import zlib from "node:zlib";
import { execFileSync } from "node:child_process";
import { readMesh } from "./dmx-binary.mjs";

const [out, meshFile, refDir] = process.argv.slice(2);
const previewAt = process.argv.indexOf("--preview");
const preview = previewAt > 0 ? process.argv[previewAt + 1] : null;
if (!refDir) { console.error("usage: generate-ball-skins.mjs <addon content dir> <ball dmx> <reference dir> [--preview <png>]"); process.exit(1); }

const MODEL = "models/soccermod/ball/wc_ball", MAT_DIR = "materials/soccermod/ball", ORIGINAL_MAT = "materials/models/jabulani/jabulani.vmat";
const SIZE = 2048;
const SKINS = [{ key: "trionda", turntable: "trionda-turntable.png" }];

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

// ---- the turntable ----------------------------------------------------------------
// Grid of balls on a white background: found as connected non-white blobs of ball size.
function findBallsInGrid(img) {
  const { w, h } = img, fg = new Uint8Array(w * h), seen = new Uint8Array(w * h), balls = [];
  for (let i = 0; i < w * h; i++) fg[i] = 3 - (img.rgb[i * 3] + img.rgb[i * 3 + 1] + img.rgb[i * 3 + 2]) > 0.18 ? 1 : 0;
  for (let i = 0; i < w * h; i++) {
    if (!fg[i] || seen[i]) continue;
    const st = [i]; seen[i] = 1; let n = 0, x0 = 1e9, x1 = -1, y0 = 1e9, y1 = -1;
    while (st.length) {
      const p = st.pop(); n++; const x = p % w, y = (p / w) | 0;
      x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y);
      for (const q of [p - 1, p + 1, p - w, p + w]) if (q >= 0 && q < w * h && fg[q] && !seen[q]) { seen[q] = 1; st.push(q); }
    }
    if (n > 8000) balls.push({ cx: (x0 + x1) / 2, cy: (y0 + y1) / 2, r: Math.min(x1 - x0, y1 - y0) / 2 - 1 });
  }
  return balls;
}
const RAD = Math.PI / 180;
const rotZ = (v, a) => [v[0] * Math.cos(a) - v[1] * Math.sin(a), v[0] * Math.sin(a) + v[1] * Math.cos(a), v[2]];
const rotY = (v, a) => [v[0] * Math.cos(a) + v[2] * Math.sin(a), v[1], -v[0] * Math.sin(a) + v[2] * Math.cos(a)];

// The 14 views are NOT a rigid turntable (the image is AI-made, the ball drifts from view to
// view), so every view gets its own pose: a rotation R_k (ball frame -> camera frame; camera
// on +x, screen right = +y, up = +z) found by registering it against the views placed before
// it (the colours of both must agree where both see a direction well). Views are placed in
// order 0..11 (neighbours overlap), then top and bottom.
const rotX = (v, a) => [v[0], v[1] * Math.cos(a) - v[2] * Math.sin(a), v[1] * Math.sin(a) + v[2] * Math.cos(a)];
const matFor = (yaw, pitch, roll) => {            // R = Rz(yaw) Ry(pitch) Rx(roll), as three basis images
  const cols = [[1, 0, 0], [0, 1, 0], [0, 0, 1]].map((e) => rotZ(rotY(rotX(e, roll * RAD), pitch * RAD), yaw * RAD));
  return [[cols[0][0], cols[1][0], cols[2][0]], [cols[0][1], cols[1][1], cols[2][1]], [cols[0][2], cols[1][2], cols[2][2]]];
};
const matVec = (m, v) => [m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2], m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2], m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2]];

function turntableProjector(img) {
  const found = findBallsInGrid(img);
  if (found.length !== 14) throw new Error('turntable: expected 14 balls, found ' + found.length);
  const rows = [...found].sort((a, b) => a.cy - b.cy);
  const row1 = rows.slice(0, 6).sort((a, b) => a.cx - b.cx), row2 = rows.slice(6, 12).sort((a, b) => a.cx - b.cx);
  const balls = [...row1, ...row2, ...rows.slice(12).sort((a, b) => a.cx - b.cx)];   // 0..11 around, 12 top, 13 bottom
  const poses = new Array(14).fill(null);
  const sampleAt = (k, d, R = poses[k]) => {
    const v = matVec(R, d), ball = balls[k], gain = 1 / (0.8 + 0.2 * Math.max(0, v[0]));
    return { facing: v[0], c: bilinear(img, ball.cx + v[1] * ball.r, ball.cy - v[2] * ball.r).map((x) => Math.min(1, x * gain)) };
  };
  const fr = (x) => { const v = Math.sin(x) * 43758.5453; return v - Math.floor(v); };
  const dirs = Array.from({ length: 700 }, (_, i) => norm([fr(i * 12.9898 + 1) - 0.5, fr(i * 78.233 + 2) - 0.5, fr(i * 37.719 + 3) - 0.5]));
  const dist = (p, q) => Math.abs(p[0] - q[0]) + Math.abs(p[1] - q[1]) + Math.abs(p[2] - q[2]);
  const MINF = 0.45;
  const costOf = (k, R, refs, pts) => {
    let sum = 0, n = 0;
    for (const d of pts) {
      const v = matVec(R, d);
      if (v[0] < MINF) continue;
      const mine = sampleAt(k, d, R).c;
      for (const j of refs) { const o = sampleAt(j, d); if (o.facing >= MINF) { sum += dist(mine, o.c); n++; } }
    }
    return n < 80 ? 9 : sum / n;
  };
  poses[0] = matFor(0, 0, 0);
  const order = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13];
  for (const k of order) {
    const refs = k >= 12 ? [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11] : [k - 1, k - 2, k - 3].filter((j) => j >= 0).concat(k === 11 ? [0, 1] : []);
    const pts = dirs.slice(0, 350);
    let best = { cost: 9, yaw: 0, pitch: 0, roll: 0 };
    const pitchRange = k >= 12 ? [-120, 120] : [-40, 40];
    for (let yaw = 0; yaw < 360; yaw += 8) for (let pitch = pitchRange[0]; pitch <= pitchRange[1]; pitch += 8) for (let roll = (k >= 12 ? -180 : -40); roll <= (k >= 12 ? 180 : 40); roll += 8) {
      const c = costOf(k, matFor(yaw, pitch, roll), refs, pts);
      if (c < best.cost) best = { cost: c, yaw, pitch, roll };
    }
    for (const step of [4, 2, 1]) {
      let improved = true;
      while (improved) {
        improved = false;
        for (const [dy, dp, dr] of [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]]) {
          const cand = { yaw: best.yaw + dy * step, pitch: best.pitch + dp * step, roll: best.roll + dr * step };
          const c = costOf(k, matFor(cand.yaw, cand.pitch, cand.roll), refs, dirs);
          if (c < best.cost - 1e-4) { best = { cost: c, ...cand }; improved = true; }
        }
      }
    }
    poses[k] = matFor(best.yaw, best.pitch, best.roll);
    console.log('view ' + k + ': yaw ' + best.yaw + ' pitch ' + best.pitch + ' roll ' + best.roll + ' colour difference ' + best.cost.toFixed(3));
  }
  // second look: every view (except 0, which fixes the frame) is nudged against ALL others, twice
  const fits = new Array(14).fill(0);
  for (let pass = 0; pass < 2; pass++) for (let k = 1; k < 14; k++) {
    const refs = [...Array(14).keys()].filter((j) => j !== k);
    let cur = { yaw: 0, pitch: 0, roll: 0 };
    // the pose as angles is not stored, so search small rotations on top of the current pose
    let R = poses[k], bestC = costOf(k, R, refs, dirs);
    for (const step of [3, 1.5, 0.75]) {
      let improved = true;
      while (improved) {
        improved = false;
        for (const [dy, dp, dr] of [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]]) {
          const delta = matFor(dy * step, dp * step, dr * step);
          const cand = delta.map((row) => [0, 1, 2].map((c) => row[0] * R[0][c] + row[1] * R[1][c] + row[2] * R[2][c]));
          const c = costOf(k, cand, refs, dirs);
          if (c < bestC - 1e-4) { bestC = c; R = cand; improved = true; }
        }
      }
    }
    poses[k] = R; fits[k] = bestC;
  }
  console.log('after joint pass, colour differences: ' + fits.slice(1).map((v) => v.toFixed(2)).join(' '));
  // the projector: the views facing the direction best, blended only where they are about equally good
  return (d) => {
    const views = [];
    for (let k = 0; k < 14; k++) { const v = sampleAt(k, d); if (v.facing > 0.2) views.push({ f: v.facing, c: v.c }); }
    if (!views.length) return [1, 1, 1];
    views.sort((p, q) => q.f - p.f);
    let acc = [0, 0, 0], ws = 0;
    for (const v of views.slice(0, 2)) { const w = Math.exp((v.f - views[0].f) / 0.012); for (let q = 0; q < 3; q++) acc[q] += v.c[q] * w; ws += w; }
    return acc.map((x) => x / ws);
  };
}

// ---- the map ball's mesh: per-corner position + uv -------------------------------
const mesh = readMesh(meshFile);
const tris = [];
for (const face of mesh.faces) for (let k = 1; k + 1 < face.length; k++) tris.push([face[0], face[k], face[k + 1]]);
const P = (corner) => mesh.positions[mesh.positionIndices[corner]];
const UV = (corner) => mesh.uvs[mesh.uvIndices[corner]];
const norm = (v) => { const l = Math.hypot(v[0], v[1], v[2]); return [v[0] / l, v[1] / l, v[2] / l]; };

// The engine reads image row = v * height (v = 0 is the top row), measured 2026-10-18 on the map ball's own texture.
// Texel -> direction map of the UV layout (filled triangles, then a few dilation passes).
function directionMap() {
  const dirs = new Float32Array(SIZE * SIZE * 3), hit = new Uint8Array(SIZE * SIZE);
  for (const t of tris) {
    const uv = t.map((c) => { const u = UV(c); return [u[0] * SIZE, u[1] * SIZE]; });
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
const textures = SKINS.map((skin) => {
  const buf = Buffer.alloc(SIZE * SIZE * 3, 255);
  const project = turntableProjector(loadImage(path.join(refDir, skin.turntable)));
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
          const tx = Math.max(0, Math.min(SIZE - 1, Math.floor(u * SIZE))), ty = Math.max(0, Math.min(SIZE - 1, Math.floor(v * SIZE))), to = (ty * SIZE + tx) * 3;
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
