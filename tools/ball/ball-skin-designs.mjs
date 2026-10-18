// Design functions for tools/ball/generate-ball-skins.mjs: color(dir) -> [r,g,b]
// (0..1) for a unit direction on the ball. Panels are the spherical Voronoi
// cells of the ball's panel centres, so seams lie on great-circle arcs; the
// graphics follow the owner's ChatGPT reference images (2026-10-18, kept in
// Documents/AI/ball-skins-ref, not shipped).

// ---- vector helpers -------------------------------------------------------------
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = (v) => { const l = Math.hypot(v[0], v[1], v[2]); return [v[0] / l, v[1] / l, v[2] / l]; };
const mix = (a, b, t) => [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t];
const clamp01 = (t) => Math.max(0, Math.min(1, t));
const smooth = (e0, e1, x) => { const t = clamp01((x - e0) / (e1 - e0)); return t * t * (3 - 2 * t); };
const hex = (h) => [parseInt(h.slice(1, 3), 16) / 255, parseInt(h.slice(3, 5), 16) / 255, parseInt(h.slice(5, 7), 16) / 255];
const shade = (c, k) => [c[0] * k, c[1] * k, c[2] * k];
const TAU = Math.PI * 2;

// Orthonormal frame round a panel centre; `ref` fixes the azimuth zero.
function frame(c, ref = [0, 0, 1]) {
  let u = cross(ref, c);
  if (Math.hypot(...u) < 1e-6) u = cross([1, 0, 0], c);
  u = norm(u);
  return { c, u, v: cross(c, u) };
}
// angle from the centre (rad) and azimuth (rad) of d in a panel frame
const polar = (d, f) => [Math.acos(Math.max(-1, Math.min(1, dot(d, f.c)))), Math.atan2(dot(d, f.v), dot(d, f.u))];

// Nearest panel centre and the seam distance (difference of the two best dots ~ angle to the seam).
function panelOf(d, centres) {
  let best = -2, second = -2, idx = 0;
  centres.forEach((c, i) => { const k = dot(d, c); if (k > best) { second = best; best = k; idx = i; } else if (k > second) second = k; });
  return { idx, seam: best - second };
}

// Seam groove: thin light-grey line with a soft shadow, as thermally bonded panels look.
function seam(col, s, width = 0.006) {
  const line = 1 - smooth(width * 0.5, width, s);
  const groove = 1 - smooth(width, width * 3.5, s);
  return mix(shade(col, 1 - 0.10 * groove), hex("#bfbfbf"), line * 0.85);
}

// Cheap value noise on the sphere (for printed texture / grain).
function hash3(x, y, z) { const s = Math.sin(x * 127.1 + y * 311.7 + z * 74.7) * 43758.5453; return s - Math.floor(s); }
function noise(d, f) {
  const p = [d[0] * f, d[1] * f, d[2] * f], i = p.map(Math.floor), t = p.map((v, k) => { const r = v - i[k]; return r * r * (3 - 2 * r); });
  let out = 0;
  for (let dx = 0; dx < 2; dx++) for (let dy = 0; dy < 2; dy++) for (let dz = 0; dz < 2; dz++)
    out += hash3(i[0] + dx, i[1] + dy, i[2] + dz) * (dx ? t[0] : 1 - t[0]) * (dy ? t[1] : 1 - t[1]) * (dz ? t[2] : 1 - t[2]);
  return out;
}

// 5-point star SDF-ish test in a 2D cell (r = outer radius)
function inStar(x, y, r) {
  const a = Math.atan2(y, x), d = Math.hypot(x, y);
  const k = ((a % (TAU / 5)) + TAU / 5) % (TAU / 5) - TAU / 10;
  return d < r * (0.48 + 0.52 * Math.pow(Math.abs(Math.cos(k * 2.5)), 6));
}

// ---- polyhedra ---------------------------------------------------------------------
const TETRA = [[1, 1, 1], [1, -1, -1], [-1, 1, -1], [-1, -1, 1]].map(norm);
const OCTA = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];

const WHITE = hex("#f3f3f0");

// ---- Trionda (2026): 4 panels; on each a big curved triangle swirling round the
// panel centre - red (maple leaves), blue (stars), green (eagle feathers), with a
// thin gold edge; white between them.
const TRI_COLORS = [hex("#d8262f"), hex("#1f5fc8"), hex("#2f9d45"), hex("#1f5fc8")];
const TRI_FRAMES = TETRA.map((c, i) => frame(c, TETRA[(i + 1) % 4]));
function trionda(d) {
  const { idx, seam: s } = panelOf(d, TETRA);
  const f = TRI_FRAMES[idx], [a, th] = polar(d, f);
  // rounded triangle in polar form, twisted with the distance (the swirl)
  const tw = th + a * 0.9, sector = ((tw % (TAU / 3)) + TAU / 3) % (TAU / 3) - TAU / 6;
  const tri = 1.38 * Math.cos(Math.PI / 3) / Math.cos(sector * 0.72);
  const edge = tri - a;                                 // > 0 inside the lobe
  let col = WHITE;
  if (edge > -0.02) {
    const base = TRI_COLORS[idx];
    // darker towards the tips, lighter near the centre; printed grain
    let c = shade(base, 0.78 + 0.32 * smooth(0.0, 0.5, edge) + 0.10 * (noise(d, 18) - 0.5));
    // motif: blue stars, red leaves (diamonds), green feather stripes
    const gx = dot(d, f.u) * 9, gy = dot(d, f.v) * 9, cx = gx - Math.round(gx), cy = gy - Math.round(gy);
    const motif = idx === 0 ? Math.abs(cx) + Math.abs(cy) < 0.28 + 0.1 * Math.sin(gx * 3)
      : idx === 2 ? Math.sin((a * 26 + tw * 4)) > 0.75
        : inStar(cx, cy, 0.3);
    if (motif) c = mix(c, [1, 1, 1], 0.22);
    const goldRim = 1 - smooth(0.0, 0.012, Math.abs(edge - 0.006));
    col = mix(c, WHITE, smooth(0.0, -0.02, edge));
    col = mix(col, hex("#d1ab4f"), goldRim * 0.9);
  }
  return seam(col, s);
}

// ---- Al Rihla (2022): 8 big white triangles (the octants; their edges are the three
// coordinate great circles) with brush-stroke bands along every edge - a blue line on
// the seam, then red, orange, yellow fading out in halftone streaks; blue stars round
// the 6 corners.
const AR = { blue: hex("#1f63d8"), sky: hex("#3aa6f2"), red: hex("#e3262b"), orange: hex("#f5871f"), yellow: hex("#f7d43a") };
function alRihla(d) {
  const ab = d.map(Math.abs), order = [0, 1, 2].sort((p, q) => ab[p] - ab[q]);
  const e = ab[order[0]], m = ab[order[1]];               // to the edge, to the corner along it
  const along = Math.atan2(d[order[2]], d[order[1]]);     // position along the edge, for the strokes
  const strokes = 0.8 + 0.2 * Math.sin(along * 30 + 3 * Math.sin(along * 7)) * (0.5 + 0.5 * noise(d, 9));
  const corner = 1 - smooth(0.06, 0.26, m);               // 1 at the octahedron corners
  const width = 0.36 * strokes * (1 - 0.4 * corner) + 0.02;
  const streak = 0.5 + 0.5 * Math.sin(along * 160 + 6 * noise(d, 14));   // brush streaks across the band
  const t = e / width;
  let col = WHITE;
  if (t < 1.25) {
    const ramp = t < 0.07 ? AR.blue : t < 0.42 ? mix(AR.red, AR.orange, smooth(0.28, 0.42, t))
      : t < 0.75 ? mix(AR.orange, AR.yellow, smooth(0.6, 0.75, t)) : AR.yellow;
    const blueCorner = mix(AR.blue, AR.sky, smooth(0.0, 0.2, e));
    const c = mix(ramp, blueCorner, corner * (1 - smooth(0.1, 0.3, e + m * 0.5)));
    const streaked = mix(c, WHITE, t > 0.3 ? 0.35 * streak * smooth(0.3, 1.0, t) : 0);
    // halftone dots in the outer half of the band
    const g = 120, dots = Math.hypot(d[order[1]] * g - Math.round(d[order[1]] * g), d[order[2]] * g - Math.round(d[order[2]] * g));
    const cover = t < 0.6 ? 1 : dots < 0.5 * (1.25 - t) ? 1 : 0;
    col = mix(WHITE, streaked, cover * (1 - smooth(1.0, 1.25, t)));
  }
  return seam(col, e * 2, 0.007);
}

// ---- Telstar 18 (2018): white, a pixel-mosaic patch across the middle of every cube
// edge (12), elongated across the edge - black / dark grey / grey squares, ragged ends.
// Seen on a face centre: four patches round a white centre, as in the reference.
const TS = [hex("#151515"), hex("#2b2b2b"), hex("#4a4a4a"), hex("#7d7d7d"), hex("#b5b5b5")];
function telstar(d) {
  const ab = d.map(Math.abs), mx = Math.max(...ab), c = d.map((v) => v / mx);   // cube surface point
  const order = [0, 1, 2].sort((p, q) => Math.abs(c[q]) - Math.abs(c[p]));       // two largest = the edge
  const i = order[0], j = order[1], k = order[2];
  const across = 1 - Math.abs(c[j]);                                                // distance from the edge on the face
  const along = c[k];                                                               // position along the edge
  const cell = 0.085, gx = Math.floor(along / cell), gy = Math.floor((Math.sign(c[j]) * across + 2 * Math.sign(c[i])) / cell);
  const r = hash3(gx * 1.7 + i * 13, gy * 2.3 + j * 7, Math.sign(c[i]) * 3 + Math.sign(c[j]));
  // patch: |along| < ~0.36, across < ~0.62; density fades at the ends (ragged)
  const fade = smooth(0.62, 0.38, across) * smooth(0.40, 0.22, Math.abs(along));
  const { seam: s } = panelOf(d, OCTA);
  let col = WHITE;
  if (r < fade * 1.15) col = TS[Math.min(4, Math.floor(hash3(gx, gy, k + 5) * 5 * (0.55 + 0.45 * (1 - fade))))];
  return seam(col, s, 0.005);
}

// ---- Jabulani (2010): white, 4 big curved bands (a rounded-triangle ring round each
// tetrahedron centre, open on one side) - near-black with fine multicolour stitch
// lines, a yellow inner edge and a thin black outline; white window inside.
const JB_FRAMES = TETRA.map((c, i) => frame(c, TETRA[(i + 2) % 4]));
const JB_LINES = [hex("#3aa34a"), hex("#e2c22a"), hex("#d23b2b"), hex("#2f6fd0"), hex("#e07a26")];
function jabulani(d) {
  const { idx, seam: s } = panelOf(d, TETRA);
  const f = JB_FRAMES[idx], [a, th] = polar(d, f);
  const sector = ((th % (TAU / 3)) + TAU / 3) % (TAU / 3) - TAU / 6;
  const r = 0.74 * Math.cos(Math.PI / 3) / Math.cos(sector * 0.78) + 0.16;   // ring centre line
  const gap = 1 - smooth(0.80, 0.97, Math.cos(th - 0.6));                   // open on one side
  const w = 0.27 * gap, inner = r - w / 2, outer = r + w / 2;
  let col = WHITE;
  if (w > 0.01 && a > inner - 0.02 && a < outer + 0.012) {
    if (a < inner) col = hex("#f1c21b");                                      // yellow inner edge
    else if (a > outer) col = hex("#111111");                                 // black outline
    else {
      const stitch = Math.sin(th * 260 + a * 90) * Math.sin(th * 40 - a * 300);
      col = stitch > 0.55 ? JB_LINES[Math.floor(hash3(Math.floor(th * 60), Math.floor(a * 60), idx) * 5)] : hex("#151515");
      col = shade(col, 0.9 + 0.1 * noise(d, 40));
    }
  }
  return seam(col, s, 0.005);
}

// ---- SoccerMod (own design, ChatGPT 2026-10-18): 4 white panels; a navy band on every
// seam, swelling into a three-armed star where three panels meet, flanked by orange
// flames near the star and thin gold pinstripes.
function soccermod(d) {
  const ks = TETRA.map((c) => dot(d, c)).sort((p, q) => q - p);
  const s = ks[0] - ks[1];                                  // to the nearest seam
  const s2 = ks[0] - ks[2];                                 // small near a three-panel junction
  const star = 1 - smooth(0.0, 1.15, s2);
  const navyW = 0.04 + 0.30 * Math.pow(star, 1.6);
  const flameW = navyW + 0.03 + 0.26 * star * (0.8 + 0.2 * Math.sin(s2 * 14));
  let col = WHITE;
  if (s < navyW) col = shade(hex("#14295a"), 0.92 + 0.12 * noise(d, 30));
  else if (s < navyW + 0.012) col = hex("#c9a54e");          // gold pinstripe on the band
  else if (s < flameW && star > 0.15) col = mix(hex("#ff5a12"), hex("#ff8a1f"), smooth(navyW, flameW, s));
  else if (Math.abs(s - flameW - 0.02) < 0.005 && star > 0.15) col = hex("#c9a54e");
  return seam(col, s, 0.004);
}

// ---- placeholders until their references are worked in -----------------------------
const plain = (accent) => (d) => {
  const { seam: s } = panelOf(d, OCTA);
  return seam(mix(WHITE, hex(accent), smooth(0.55, 0.6, Math.abs(d[2]))), s);
};

export const DESIGNS = [
  { key: "trionda", name: "Trionda 2026", color: trionda },
  { key: "alrihla", name: "Al Rihla 2022", color: alRihla },
  { key: "telstar18", name: "Telstar 18", color: telstar },
  { key: "jabulani", name: "Jabulani 2010", color: jabulani },
  { key: "soccermod", name: "SoccerMod", color: soccermod },
];
