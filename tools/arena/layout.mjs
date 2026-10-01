// SoccerMod Arena: the shape of the new stadium around the unchanged v8 pitch.
//
// Everything outside the pitch walls hangs on one closed curve, the front of the
// lower stand (a superellipse). A point of the stadium is given as
//   (station on that curve, d = distance outwards along the curve normal, z).
// The stands, the roof, the generated crowd and the plugin positions all use
// these numbers, so they live here and nowhere else.
// Owner 2026-10-01: the map file is ka_soccermod_stadium (its first Workshop revisions were soccer_soccermod_arena).
export const MAP_NAME = "ka_soccermod_stadium";
export const PITCH = { x: 1280, y: 1664, z: -32, lineX: 1024, lineY: 1384 };

// front of the lower stand: |x/A|^n + |y/B|^n = 1 (v8 had its stand fronts at 1580 / 1964)
export const CURVE = { A: 1604, B: 2070, EXP: 4.5 };

export const AISLE = 56;            // width of a stair aisle on the front curve
export const AISLES_PER_QUADRANT = 6;
export const TUNNEL_HALF = 104;     // half width of the tunnel head at the middle of each long side

// ---- cross-section (d outwards from the front curve, z up) ---------------------------------
export const FLOOR = PITCH.z;
export const PARAPET = { top: 48, depth: 10, adLow: -26, adHigh: 38 };
export const WALK0 = { d0: 10, d1: 66, z: 40 };
export const LOWER = { rows: 16, depth: 32, rise: 15, d0: 66, z0: 40 };          // tread k at z0 + rise * (k + 1)
export const LOWER_TOP = { d0: LOWER.d0 + LOWER.rows * LOWER.depth, d1: 650, z: LOWER.z0 + LOWER.rise * (LOWER.rows + 1) };
export const BOXES = { d: LOWER_TOP.d1, z0: LOWER_TOP.z, z1: 425, recess: 90 };
export const FASCIA = { d: 560, z0: BOXES.z1, z1: 505, ledLow: 443, ledHigh: 487, cap: 12, railTop: 533 };
export const WALK1 = { d0: FASCIA.d + FASCIA.cap, d1: 620, z: 497 };
export const UPPER = { rows: 20, depth: 30, rise: 21, d0: WALK1.d1, z0: WALK1.z, vomitoryRows: 5 };
export const UPPER_TOP = { d0: UPPER.d0 + UPPER.rows * UPPER.depth, d1: 1290, z: UPPER.z0 + UPPER.rise * (UPPER.rows + 1) };
export const BACKWALL = { d0: UPPER_TOP.d1, d1: 1330, top: UPPER_TOP.z + 110 };
// roof: u = 0 at the eave (outside the back wall) .. 1 at the inner edge over the lower stand
export const ROOF = {
  dEave: 1400, dInner: 230, glassFrom: 0.62,
  backLow: 1120, backWave: 140,   // height over the back wall: low at the ends, high over the long sides
  rise: 130, arch: 40, deck: 8,
};
export const SEAT = { width: 21 };

const { A, B, EXP } = CURVE;
const radius = (a) => (Math.abs(Math.cos(a) / A) ** EXP + Math.abs(Math.sin(a) / B) ** EXP) ** (-1 / EXP);

// ---- one quadrant of the front curve, from the middle of the east side to the middle of the north end
function quadrant() {
  const M = 40000, pts = [];
  let len = 0;
  for (let i = 0; i <= M; i++) {
    const a = (i / M) * Math.PI / 2, r = radius(a), x = r * Math.cos(a), y = r * Math.sin(a);
    if (i) len += Math.hypot(x - pts[i - 1].x, y - pts[i - 1].y);
    // outward normal = gradient of the superellipse
    let nx = x ** (EXP - 1) / A ** EXP, ny = y ** (EXP - 1) / B ** EXP;
    const nl = Math.hypot(nx, ny); nx /= nl; ny /= nl;
    pts.push({ x, y, nx, ny, s: len, phi: Math.atan2(ny, nx) });
  }
  const at = (s) => {
    let lo = 0, hi = M;
    while (hi - lo > 1) { const mid = (lo + hi) >> 1; if (pts[mid].s <= s) lo = mid; else hi = mid; }
    const a = pts[lo], b = pts[hi], t = b.s > a.s ? (s - a.s) / (b.s - a.s) : 0;
    const L = (k) => a[k] + (b[k] - a[k]) * t;
    const nl = Math.hypot(L("nx"), L("ny"));
    return { x: L("x"), y: L("y"), nx: L("nx") / nl, ny: L("ny") / nl, s, phi: L("phi") };
  };
  return { length: len, at };
}

// Stations of one quadrant: blocks of seats separated by aisles, block centres on both axes.
function quadrantStations() {
  const q = quadrant(), L = q.length, k = AISLES_PER_QUADRANT;
  const block = (L - k * AISLE) / k;
  const spans = [];   // [s0, s1, kind]
  let s = 0;
  spans.push([0, TUNNEL_HALF, "tunnel"]);
  spans.push([TUNNEL_HALF, block / 2, "block"]);
  s = block / 2;
  for (let a = 0; a < k; a++) {
    spans.push([s, s + AISLE, "aisle"]); s += AISLE;
    const len = a === k - 1 ? block / 2 : block;
    spans.push([s, s + len, "block"]); s += len;
  }
  const stations = [q.at(0)], kinds = [];
  for (const [s0, s1, kind] of spans) {
    // equal steps of (length / 130 + turn / 2.5 degrees), so the corners get the short segments
    const cost = (x) => (x - s0) / 130 + Math.abs(q.at(x).phi - q.at(s0).phi) * 180 / Math.PI / 2.5;
    const total = cost(s1);
    const n = kind === "aisle" ? 1 : Math.max(1, Math.ceil(total - 1e-9));
    for (let j = 1; j <= n; j++) {
      let lo = s0, hi = s1; const want = total * j / n;
      for (let it = 0; it < 50; it++) { const mid = (lo + hi) / 2; if (cost(mid) < want) lo = mid; else hi = mid; }
      stations.push(q.at(j === n ? s1 : (lo + hi) / 2)); kinds.push(kind);
    }
  }
  return { stations, kinds, length: L, block };
}

// ---- the whole ring, counter-clockwise from the middle of the east side ----------------------
function ring() {
  const q = quadrantStations(), m = q.kinds.length, st = [], kinds = [];
  const mirror = (p, sx, sy) => ({ x: p.x * sx, y: p.y * sy, nx: p.nx * sx, ny: p.ny * sy });
  const add = (order, sx, sy, kindOrder) => { for (const i of order) st.push(mirror(q.stations[i], sx, sy)); for (const i of kindOrder) kinds.push(q.kinds[i]); };
  const fwd = [...Array(m).keys()], rev = fwd.map((i) => m - i), revK = fwd.map((i) => m - 1 - i);
  add(fwd, 1, 1, fwd); add(rev, -1, 1, revK); add(fwd, -1, -1, fwd); add(rev, 1, -1, revK);
  const N = st.length;
  let S = 0;
  st.forEach((p, i) => { p.i = i; p.S = S; const b = st[(i + 1) % N]; S += Math.hypot(b.x - p.x, b.y - p.y); p.phi = Math.atan2(p.ny, p.nx); });
  // blocks: runs of seat segments between two aisles (the run through station 0 wraps round)
  const blockOf = new Array(N).fill(-1);
  let start = kinds.findIndex((k, i) => k !== "aisle" && kinds[(i + N - 1) % N] === "aisle"), id = 0;
  const blocks = [];
  for (let c = 0, i = start; c < N; c++, i = (i + 1) % N) {
    if (kinds[i] === "aisle") { if (kinds[(i + 1) % N] !== "aisle") id++; continue; }
    blockOf[i] = id; (blocks[id] ??= []).push(i);
  }
  return { stations: st, kinds, N, perimeter: S, blockOf, blocks, quadrantSegments: m };
}

export const RING = ring();
const { stations: ST, N } = RING;

export const station = (i) => ST[((i % N) + N) % N];
// point at station i (fractional allowed), d outwards, height z
export function P(i, d, z) {
  const i0 = Math.floor(i), t = i - i0;
  const a = station(i0);
  if (t < 1e-9) return [a.x + a.nx * d, a.y + a.ny * d, z];
  const b = station(i0 + 1);
  return [a.x + a.nx * d + (b.x + b.nx * d - a.x - a.nx * d) * t, a.y + a.ny * d + (b.y + b.ny * d - a.y - a.ny * d) * t, z];
}
// 0 on the long sides, 1 at the ends
export const endness = (i) => { const s = station(Math.round(i)); return s.ny * s.ny; };
// which side a station looks from: "east" | "north" | "west" | "south"
export const sideOf = (i) => {
  const s = station(Math.round(i));
  return Math.abs(s.nx) >= Math.abs(s.ny) ? (s.nx > 0 ? "east" : "west") : (s.ny > 0 ? "north" : "south");
};
// roof heights at a station
export const roofBack = (i) => { const s = station(Math.round(i)); return ROOF.backLow + ROOF.backWave * s.nx * s.nx; };
export const roofD = (u) => ROOF.dEave + (ROOF.dInner - ROOF.dEave) * u;
export const roofZ = (i, u) => roofBack(i) + ROOF.rise * u + ROOF.arch * Math.sin(Math.PI * u);
// seat rows: tread height and the d range of row k
export const lowerRow = (k) => ({ z: LOWER.z0 + LOWER.rise * (k + 1), d0: LOWER.d0 + LOWER.depth * k, d1: LOWER.d0 + LOWER.depth * (k + 1) });
export const upperRow = (k) => ({ z: UPPER.z0 + UPPER.rise * (k + 1), d0: UPPER.d0 + UPPER.depth * k, d1: UPPER.d0 + UPPER.depth * (k + 1) });
// trusses: one over every aisle and one over the middle of every block
export const TRUSS_STATIONS = (() => {
  const out = [];
  for (let i = 0; i < N; i++) if (RING.kinds[i] === "aisle") out.push(i);
  for (const b of RING.blocks) out.push(b[Math.floor(b.length / 2)]);
  return [...new Set(out)].sort((a, b) => a - b);
})();
