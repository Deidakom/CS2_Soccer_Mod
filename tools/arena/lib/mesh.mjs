// Polygon soup grouped by material, with flat normals and explicit UVs.
// Faces are stored counter-clockwise seen from the front (what the DMX wants).
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const norm = (a) => { const l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };

export const vec = { sub, cross, dot, norm, add: (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]], mul: (a, k) => [a[0] * k, a[1] * k, a[2] * k],
  lerp: (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t] };

export function polyNormal(pts) {
  // Newell: robust for slightly non-planar quads
  let n = [0, 0, 0];
  for (let i = 0; i < pts.length; i++) {
    const a = pts[i], b = pts[(i + 1) % pts.length];
    n[0] += (a[1] - b[1]) * (a[2] + b[2]); n[1] += (a[2] - b[2]) * (a[0] + b[0]); n[2] += (a[0] - b[0]) * (a[1] + b[1]);
  }
  return norm(n);
}

export class Scene {
  constructor() { this.faces = []; }
  // pts: 3+ points, uvs: one [u, v] per point. facing: a direction the front must look along
  // (the winding is flipped when it does not). opts: { solid, group }
  poly(material, pts, uvs, facing, opts = {}) {
    let p = pts, t = uvs, n = polyNormal(p);
    if (facing && dot(n, facing) < 0) { p = [...p].reverse(); t = [...t].reverse(); n = [-n[0], -n[1], -n[2]]; }
    for (const q of p) if (!q.every(Number.isFinite)) throw new Error(`bad vertex in ${material}`);
    const area = polyArea(p);
    if (area < 1e-3) return;
    this.faces.push({ material, pts: p, uvs: t, n, solid: !!opts.solid, group: opts.group ?? null, twoSided: !!opts.twoSided, physOnly: !!opts.physOnly });
  }
  quad(material, a, b, c, d, uv, facing, opts) { this.poly(material, [a, b, c, d], uv, facing, opts); }
  // axis-free box from 8 corners: bottom ring b[0..3] and top ring t[0..3] in the same order;
  // sides: which faces to emit ("s0".."s3" = side between corner k and k+1, "top", "bottom")
  prism(material, b, t, uvScale, opts = {}, skip = []) {
    const centre = [...b, ...t].reduce((s, p) => [s[0] + p[0] / 8, s[1] + p[1] / 8, s[2] + p[2] / 8], [0, 0, 0]);
    const face = (pts, name) => {
      if (skip.includes(name)) return;
      const n = polyNormal(pts), c = pts.reduce((s, p) => [s[0] + p[0] / pts.length, s[1] + p[1] / pts.length, s[2] + p[2] / pts.length], [0, 0, 0]);
      const out = sub(c, centre), facing = dot(n, out) >= 0 ? n : [-n[0], -n[1], -n[2]];
      // planar UVs along the face's own axes
      const ux = norm(sub(pts[1], pts[0])), uy = cross(facing, ux);
      this.poly(material, pts, pts.map((p) => [dot(sub(p, pts[0]), ux) / uvScale, dot(sub(p, pts[0]), uy) / uvScale]), facing, opts);
    };
    for (let k = 0; k < 4; k++) face([b[k], b[(k + 1) % 4], t[(k + 1) % 4], t[k]], `s${k}`);
    face([t[0], t[1], t[2], t[3]], "top"); face([b[0], b[1], b[2], b[3]], "bottom");
  }
  stats() {
    const by = {};
    for (const f of this.faces) { const e = (by[f.material] ??= { faces: 0, tris: 0 }); e.faces++; e.tris += f.pts.length - 2; }
    return by;
  }
}

export function polyArea(p) {
  let a = [0, 0, 0];
  for (let i = 1; i + 1 < p.length; i++) { const c = cross(sub(p[i], p[0]), sub(p[i + 1], p[0])); a = [a[0] + c[0], a[1] + c[1], a[2] + c[2]]; }
  return Math.hypot(a[0], a[1], a[2]) / 2;
}
