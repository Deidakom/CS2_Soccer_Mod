// SoccerMod Arena: all the geometry outside the pitch walls, as faces grouped by material.
// layout.mjs holds the numbers; this file turns them into stands, roof, screens and floors.
import {
  PITCH, CURVE, RING, P, station, endness, sideOf, roofBack, roofD, roofZ, lowerRow, upperRow, TRUSS_STATIONS,
  FLOOR, PARAPET, WALK0, LOWER, LOWER_TOP, BOXES, FASCIA, WALK1, UPPER, UPPER_TOP, BACKWALL, ROOF, SEAT, TUNNEL_HALF,
} from "./layout.mjs";
import { Scene, vec } from "./lib/mesh.mjs";

const { N, kinds, blocks, blockOf, stations: ST } = RING;
const mod = (i) => ((i % N) + N) % N;
const UP = [0, 0, 1], DOWN = [0, 0, -1];
const M = (name) => `materials/soccermod_arena/${name}.vmat`;

export const SKY = { x: 3840, y: 4224, z0: -160, z1: 2304 };
export const VIDEO_WALL = { width: 800, height: 200, depth: 30, d: 262, drop: 44, tilt: 0, scoreV: 295 / 512, scoreX: 70 };

export function buildArena() {
  const scene = new Scene();
  // ---- helpers ---------------------------------------------------------------------------------
  const arcCache = new Map();
  // cumulative length of the ring at offset d (index N = the whole perimeter)
  const arc = (d) => {
    const key = Math.round(d);
    if (!arcCache.has(key)) {
      const a = new Float64Array(N + 1);
      for (let i = 0; i < N; i++) { const p = P(i, key, 0), q = P(i + 1, key, 0); a[i + 1] = a[i] + Math.hypot(q[0] - p[0], q[1] - p[1]); }
      arcCache.set(key, a);
    }
    return arcCache.get(key);
  };
  const inward = (i) => { const a = station(i), b = station(i + 1); return vec.norm([-(a.nx + b.nx), -(a.ny + b.ny), 0]); };
  const tangent = (i) => { const s = station(i); return [-s.ny, s.nx, 0]; };
  const facingVec = (i, f) => (f === "up" ? UP : f === "down" ? DOWN : f === "in" ? inward(i) : f === "out" ? vec.mul(inward(i), -1) : f);
  const val = (z, i) => (typeof z === "function" ? z(i) : z);
  // One ring segment of a band between (d0, z0) and (d1, z1). U runs along the ring (tile su,
  // stretched a little so the ring closes on a whole tile), V along the profile: -z / sv on
  // walls, d / sv on floors, or v0 / v1 when given (edge 0 / edge 1).
  function band(material, i, d0, z0, d1, z1, facing, o = {}) {
    const { su = 256, sv = 256, v0, v1, solid = false, group, uRef, twoSided, uTile = true } = o;
    const pts = [P(i, d0, val(z0, i)), P(i + 1, d0, val(z0, i + 1)), P(i + 1, d1, val(z1, i + 1)), P(i, d1, val(z1, i))];
    const A = arc(uRef ?? (d0 + d1) / 2), total = A[N];
    const tile = uTile ? total / Math.max(1, Math.round(total / su)) : su;
    const sgn = o.uSign ?? (facing === "in" ? -1 : 1);
    const u = [sgn * A[i] / tile, sgn * A[i + 1] / tile];
    const wall = Math.abs(val(z1, i) - val(z0, i)) > Math.abs(d1 - d0);
    const v = (edge, p) => (v0 !== undefined ? (edge ? v1 : v0) : wall ? -p[2] / sv : (edge ? d1 : d0) / sv);
    scene.quad(material, ...pts, [[u[0], v(0, pts[0])], [u[1], v(0, pts[1])], [u[1], v(1, pts[2])], [u[0], v(1, pts[3])]], facingVec(i, facing), { solid, group, twoSided });
  }
  const ringBand = (material, d0, z0, d1, z1, facing, o = {}, filter = () => true) => { for (let i = 0; i < N; i++) if (filter(i)) band(material, i, d0, z0, d1, z1, facing, o); };
  // A flat face across the ring at station i (a wall end): from (d0, z0) to (d1, z1).
  function radial(material, i, d0, z0, d1, z1, side, o = {}) {
    const t = tangent(i);
    const pts = [P(i, d0, z0), P(i, d1, z0), P(i, d1, z1), P(i, d0, z1)];
    const sv = o.sv ?? 256;
    scene.quad(material, ...pts, [[d0 / sv, -z0 / sv], [d1 / sv, -z0 / sv], [d1 / sv, -z1 / sv], [d0 / sv, -z1 / sv]], vec.mul(t, side), o);
  }
  const isAisle = (i) => kinds[mod(i)] === "aisle";
  const isTunnel = (i) => kinds[mod(i)] === "tunnel";
  const blockCentre = (b) => { const seg = blocks[b]; return station(seg[Math.floor(seg.length / 2)]); };
  // the lounge level with glass fronts: the three middle blocks of each long side
  const vipBlock = blocks.map((seg, b) => { const c = blockCentre(b); return Math.abs(c.nx) > 0.9 && Math.abs(c.y) < 900; });
  const isVip = (i) => !isAisle(i) && vipBlock[blockOf[mod(i)]];
  const isPier = (i) => isAisle(i);

  // ---- ground: surround, apron, plaza ----------------------------------------------------------
  const rectPoint = (p, rx, ry) => { const k = Math.min(rx / Math.abs(p[0] || 1e-9), ry / Math.abs(p[1] || 1e-9)); return [p[0] * k, p[1] * k]; };
  const superPoint = (p, a, b, e) => { const l = Math.hypot(p[0], p[1]), c = p[0] / l, s = p[1] / l; const r = (Math.abs(c / a) ** e + Math.abs(s / b) ** e) ** (-1 / e); return [c * r, s * r]; };
  const flat = (material, pts2, z, scale, o) => scene.poly(material, pts2.map((p) => [p[0], p[1], z]), pts2.map((p) => [p[0] / scale, -p[1] / scale]), UP, o);
  // ring between two closed curves given per station; the inner or outer one may be a rectangle
  function groundRing(material, inner, outer, z, scale, rect, o) {
    for (let i = 0; i < N; i++) {
      const a = inner(i), b = inner(i + 1), c = outer(i + 1), d = outer(i);
      if (rect) {
        const [ra, rb] = rect.which === "inner" ? [a, b] : [d, c];
        const onX = (p) => Math.abs(Math.abs(p[0]) - rect.x) < 1e-6, corner = [Math.sign(ra[0] + rb[0]) * rect.x, Math.sign(ra[1] + rb[1]) * rect.y];
        if (onX(ra) !== onX(rb) && Math.hypot(ra[0] - corner[0], ra[1] - corner[1]) > 1e-3 && Math.hypot(rb[0] - corner[0], rb[1] - corner[1]) > 1e-3) {
          if (rect.which === "inner") { flat(material, [a, corner, c, d], z, scale, o); flat(material, [corner, b, c], z, scale, o); }
          else { flat(material, [a, b, corner, d], z, scale, o); flat(material, [b, c, corner], z, scale, o); }
          continue;
        }
      }
      flat(material, [a, b, c, d], z, scale, o);
    }
  }
  const front = (i) => { const p = P(i, 0, 0); return [p[0], p[1]]; };
  const pitchEdge = (i) => rectPoint(front(i), PITCH.x, PITCH.y);
  const SURROUND = { a: 1452, b: 1868, e: 9 };
  const surround = (i) => superPoint(front(i), SURROUND.a, SURROUND.b, SURROUND.e);
  groundRing(M("surround_turf"), pitchEdge, surround, FLOOR, 256, { which: "inner", x: PITCH.x, y: PITCH.y }, { solid: true });
  groundRing(M("apron"), surround, front, FLOOR, 256, null, { solid: true });
  const facadeFoot = (i) => { const p = P(i, BACKWALL.d1, 0); return [p[0], p[1]]; };
  const skyEdge = (i) => rectPoint(facadeFoot(i), SKY.x, SKY.y);
  groundRing(M("plaza"), facadeFoot, skyEdge, FLOOR, 384, { which: "outer", x: SKY.x, y: SKY.y }, { solid: true });

  groundRing(M("dark"), front, facadeFoot, FLOOR, 256, null, { solid: true });

  // ---- lower stand -----------------------------------------------------------------------------
  const C = M("concrete"), CW = M("concrete_wall"), STAIR = M("stair");
  ringBand(CW, 0, FLOOR, 0, PARAPET.adLow, "in", { solid: true });
  {
    // printed boards on the wall round the pitch: 4 boards per 1024 units, two sets taking turns;
    // segments are cut at the tile lines so a board never changes half way
    const A = arc(0), tiles = 2 * Math.round(A[N] / 2048), tile = A[N] / tiles;
    for (let i = 0; i < N; i++) {
      const cuts = [0]; for (let m = Math.floor(A[i] / tile) + 1; m * tile < A[i + 1] - 1e-6; m++) cuts.push((m * tile - A[i]) / (A[i + 1] - A[i])); cuts.push(1);
      for (let c = 0; c + 1 < cuts.length; c++) {
        const fa = i + cuts[c], fb = i + cuts[c + 1], aa = A[i] + (A[i + 1] - A[i]) * cuts[c], ab = A[i] + (A[i + 1] - A[i]) * cuts[c + 1];
        const set = Math.floor((aa + ab) / 2 / tile) % 2, ua = -aa / tile, ub = -ab / tile;
        scene.quad(M(set ? "ads_b" : "ads_a"), P(fa, 0, PARAPET.adLow), P(fb, 0, PARAPET.adLow), P(fb, 0, PARAPET.adHigh), P(fa, 0, PARAPET.adHigh), [[ua, 1], [ub, 1], [ub, 0], [ua, 0]], inward(i), { solid: true });
      }
    }
  }
  ringBand(M("parapet_cap"), 0, PARAPET.adHigh, 0, PARAPET.top, "in", { sv: 64, solid: true });
  ringBand(M("parapet_cap"), 0, PARAPET.top, PARAPET.depth, PARAPET.top, "up", { sv: 64, solid: true });
  ringBand(CW, PARAPET.depth, WALK0.z, PARAPET.depth, PARAPET.top, "out", { solid: true });
  ringBand(C, WALK0.d0, WALK0.z, WALK0.d1, WALK0.z, "up", { solid: true });
  const steps = (tier, rowFn, rows, skipRow, noHalf = () => false) => {
    for (let k = 0; k < rows; k++) {
      const r = rowFn(k), below = k ? rowFn(k - 1).z : tier.z0;
      for (let i = 0; i < N; i++) {
        if (skipRow(i, k)) continue;
        if (isAisle(i)) {
          // stairs: the row step split in two, yellow edge on every step
          const half = noHalf(i, k) ? below : (r.z + below) / 2, dh = r.d0 - tier.depth / 2;
          if (!noHalf(i, k)) {
            band(STAIR, i, dh, below, dh, half, "in", { su: 64, v0: 1, v1: 0.5, solid: true });
            band(STAIR, i, dh, half, r.d0, half, "up", { su: 64, v0: 0, v1: 0.5, solid: true });
          }
          band(STAIR, i, r.d0, half, r.d0, r.z, "in", { su: 64, v0: 1, v1: 0.5, solid: true });
          band(STAIR, i, r.d0, r.z, r.d1 - tier.depth / 2, r.z, "up", { su: 64, v0: 0, v1: 0.5, solid: true });
          band(C, i, r.d1 - tier.depth / 2, r.z, r.d1, r.z, "up", { solid: true });
        } else {
          band(M("riser"), i, r.d0, below, r.d0, r.z, "in", { su: 128, v0: 1, v1: 0, solid: true });
          band(C, i, r.d0, r.z, r.d1, r.z, "up", { solid: true });
        }
      }
    }
  };
  steps(LOWER, lowerRow, LOWER.rows, () => false);
  ringBand(M("riser"), LOWER_TOP.d0, lowerRow(LOWER.rows - 1).z, LOWER_TOP.d0, LOWER_TOP.z, "in", { su: 128, v0: 1, v1: 0, solid: true });
  ringBand(C, LOWER_TOP.d0, LOWER_TOP.z, LOWER_TOP.d1, LOWER_TOP.z, "up", { solid: true });

  // ---- level between the stands: lounges on the long sides, open concourse elsewhere -----------
  const B = BOXES, back = B.d + B.recess, head = B.z1 - 20;
  for (let i = 0; i < N; i++) {
    if (isPier(i)) { band(CW, i, B.d, B.z0, B.d, B.z1, "in", { solid: true }); continue; }
    if (isVip(i)) {
      const sill = B.z0 + 38, top = B.z1 - 12, room = B.d + 70;
      band(M("fascia"), i, B.d, B.z0, B.d, sill, "in", { solid: true });
      band(M("vip_glass"), i, B.d, sill, B.d, top, "in", { su: 96, v0: 1, v1: 0, solid: true, twoSided: true });
      band(M("fascia"), i, B.d, top, B.d, B.z1, "in", { solid: true });
      band(M("vip_interior"), i, room, sill, room, top, "in", { su: 512, v0: 1, v1: 0 });
      band(M("dark"), i, B.d, sill, room, sill, "up", {});
      band(M("dark"), i, B.d, top, room, top, "down", {});
      continue;
    }
    band(CW, i, B.d, head, B.d, B.z1, "in", { solid: true });
    band(M("ceiling_lights"), i, B.d, head, back, head, "down", { su: 128, sv: 128 });
    band(C, i, B.d, B.z0, back, B.z0, "up", { solid: true });
    band(M("concourse"), i, back, B.z0, back, head, "in", { su: 512, v0: 1, v1: 0, solid: true });
  }
  for (let i = 0; i < N; i++) {
    // wall ends where a pier meets an opening
    const prev = mod(i - 1), a = isPier(prev), b = isPier(i);
    if (a === b) continue;
    const open = a ? i : prev, side = a ? 1 : -1;
    if (isVip(open)) radial(M("dark"), i, B.d, B.z0 + 38, B.d + 70, B.z1 - 12, side);
    else radial(CW, i, B.d, B.z0, back, head, side, { solid: true });
  }
  ringBand(M("soffit"), FASCIA.d, B.z1, B.d, B.z1, "down", { su: 128, sv: 128 });

  // ---- upper stand -----------------------------------------------------------------------------
  const F = FASCIA;
  ringBand(M("fascia"), F.d, F.z0, F.d, F.ledLow, "in", { solid: true });
  ringBand(M("led_band"), F.d, F.ledLow, F.d, F.ledHigh, "in", { su: 64, v0: 1, v1: 0, solid: true });
  ringBand(M("fascia"), F.d, F.ledHigh, F.d, F.z1, "in", { solid: true });
  ringBand(M("parapet_cap"), F.d, F.z1, F.d + F.cap, F.z1, "up", { sv: 64, solid: true });
  ringBand(CW, F.d + F.cap, WALK1.z, F.d + F.cap, F.z1, "out", { solid: true });
  ringBand(M("glass_rail"), F.d + F.cap / 2, F.z1, F.d + F.cap / 2, F.railTop, "in", { su: 64, v0: 1, v1: 0, twoSided: true });
  ringBand(C, WALK1.d0, WALK1.z, WALK1.d1, WALK1.z, "up", { solid: true });
  const vom = (i, k) => isAisle(i) && k < UPPER.vomitoryRows;
  steps(UPPER, upperRow, UPPER.rows, vom, (i, k) => k === UPPER.vomitoryRows);
  // vomitories: the stairs start five rows up, below them a tunnel mouth
  const vomBack = upperRow(UPPER.vomitoryRows - 1).d1, vomTop = upperRow(UPPER.vomitoryRows - 1).z;
  for (let i = 0; i < N; i++) {
    if (!isAisle(i)) continue;
    band(C, i, WALK1.d1, WALK1.z, vomBack, WALK1.z, "up", { solid: true });
    band(M("portal"), i, vomBack, WALK1.z, vomBack, vomTop, "in", { v0: 1, v1: 0, uTile: false, su: 1e9, solid: true });
    for (let k = 0; k < UPPER.vomitoryRows; k++) {
      const r = upperRow(k);
      radial(CW, i, r.d0, WALK1.z, r.d1, r.z, 1, { solid: true });
      radial(CW, i + 1, r.d0, WALK1.z, r.d1, r.z, -1, { solid: true });
    }
  }
  // the portal texture shows one doorway: map it to the aisle width once
  for (const f of scene.faces) if (f.material === M("portal")) { const us = f.uvs.map((u) => u[0]), lo = Math.min(...us), hi = Math.max(...us); f.uvs = f.uvs.map((u) => [u[0] === lo ? 0 : 1, u[1]]); void hi; }
  ringBand(M("riser"), UPPER_TOP.d0, upperRow(UPPER.rows - 1).z, UPPER_TOP.d0, UPPER_TOP.z, "in", { su: 128, v0: 1, v1: 0, solid: true });
  ringBand(C, UPPER_TOP.d0, UPPER_TOP.z, UPPER_TOP.d1, UPPER_TOP.z, "up", { solid: true });
  // back wall and facade
  ringBand(CW, BACKWALL.d0, UPPER_TOP.z, BACKWALL.d0, BACKWALL.top, "in", { solid: true });
  ringBand(M("parapet_cap"), BACKWALL.d0, BACKWALL.top, BACKWALL.d1, BACKWALL.top, "up", { sv: 64, solid: true });
  // outside: a glazed ground floor with the entrances, ribbed cladding above
  ringBand(M("facade_base"), BACKWALL.d1, FLOOR, BACKWALL.d1, FLOOR + 152, "out", { su: 512, v0: 1, v1: 0, solid: true });
  ringBand(M("facade"), BACKWALL.d1, FLOOR + 152, BACKWALL.d1, BACKWALL.top, "out", { su: 256, sv: 256, solid: true });

  // ---- seats -----------------------------------------------------------------------------------
  seats(scene, arc);

  // ---- roof ------------------------------------------------------------------------------------
  const US = [0, 0.06, 0.2, 0.35, 0.5, ROOF.glassFrom, 0.74, 0.87, 1];
  for (let k = 0; k + 1 < US.length; k++) {
    const u0 = US[k], u1 = US[k + 1], glass = u0 >= ROOF.glassFrom - 1e-9;
    const top = (u) => (i) => roofZ(i, u), under = (u) => (i) => roofZ(i, u) - ROOF.deck;
    if (glass) ringBand(M("roof_glass"), roofD(u0), (i) => roofZ(i, u0) - ROOF.deck / 2, roofD(u1), (i) => roofZ(i, u1) - ROOF.deck / 2, "up", { su: 128, sv: 128, v0: u0 * 9, v1: u1 * 9, twoSided: true });
    else {
      ringBand(M("roof_metal"), roofD(u0), top(u0), roofD(u1), top(u1), "up", { su: 128, v0: u0 * 5, v1: u1 * 5 });
      ringBand(M("roof_ceiling"), roofD(u0), under(u0), roofD(u1), under(u1), "down", { su: 128, v0: u0 * 9, v1: u1 * 9 });
    }
  }
  const W = M("steel_white"), WR = M("steel_roof");
  // eave
  ringBand(W, roofD(0), (i) => roofZ(i, 0) - 26, roofD(0), (i) => roofZ(i, 0), "out", { sv: 64, v0: 1, v1: 0 });
  // inner edge beam with the floodlight strip underneath
  const eIn = roofD(1), eOut = eIn + 30, eTop = (i) => roofZ(i, 1) + 4, eBot = (i) => roofZ(i, 1) - 32;
  ringBand(WR, eIn, eBot, eIn, eTop, "in", { sv: 64, v0: 1, v1: 0 });
  ringBand(WR, eOut, eBot, eOut, (i) => roofZ(i, 1) - ROOF.deck, "out", { sv: 64, v0: 1, v1: 0 });
  ringBand(M("floodlight"), eIn, eBot, eOut, eBot, "down", { su: 64, v0: 0, v1: 1 });
  ringBand(WR, eIn, eTop, eOut, eTop, "up", { sv: 64, v0: 0, v1: 1 });
  // purlins
  for (const u of [0.2, 0.35, 0.5, 0.74, 0.87]) {
    const d = roofD(u), zt = (i) => roofZ(i, u) - ROOF.deck, zb = (i) => roofZ(i, u) - ROOF.deck - 14;
    ringBand(WR, d - 5, zb, d + 5, zb, "down", { sv: 64, v0: 0, v1: 1 });
    ringBand(WR, d - 5, zb, d - 5, zt, "in", { sv: 64, v0: 1, v1: 0 });
    ringBand(WR, d + 5, zb, d + 5, zt, "out", { sv: 64, v0: 1, v1: 0 });
  }
  // trusses, columns and facade fins
  const trussDepth = (u) => 22 + 124 * (1 - u) ** 1.5;
  for (const i of TRUSS_STATIONS) {
    const t = tangent(i), uCol = (ROOF.dEave - (BACKWALL.d0 + 20)) / (ROOF.dEave - ROOF.dInner);
    const us = []; for (let k = 0; k <= 14; k++) us.push(0.06 + (1 - 0.06) * k / 14);
    let run = 0;
    for (let k = 0; k + 1 < us.length; k++) {
      const [ua, ub] = [us[k], us[k + 1]];
      const ta = P(i, roofD(ua), roofZ(i, ua) - ROOF.deck), tb = P(i, roofD(ub), roofZ(i, ub) - ROOF.deck);
      const ba = [ta[0], ta[1], ta[2] - trussDepth(ua)], bb = [tb[0], tb[1], tb[2] - trussDepth(ub)];
      const len = Math.hypot(tb[0] - ta[0], tb[1] - ta[1]), u0 = run / 150, u1 = (run += len) / 150;
      scene.quad(M("truss"), ta, tb, bb, ba, [[u0, 0], [u1, 0], [u1, 1], [u0, 1]], t, { twoSided: true });
      // chords: square tubes along the top and the bottom line
      for (const [pa, pb, h] of [[ba, bb, 9], [[ta[0], ta[1], ta[2] - 9], [tb[0], tb[1], tb[2] - 9], 9]]) {
        const off = (p, s, dz) => [p[0] + t[0] * 5 * s, p[1] + t[1] * 5 * s, p[2] + dz];
        scene.prism(WR, [off(pa, -1, 0), off(pb, -1, 0), off(pb, 1, 0), off(pa, 1, 0)], [off(pa, -1, h), off(pb, -1, h), off(pb, 1, h), off(pa, 1, h)], 64, {}, ["s1", "s3", "top"]);
      }
    }
    // column on the back wall
    const cz = roofZ(i, uCol) - ROOF.deck, col = (dd, s, z) => { const p = P(i, BACKWALL.d0 + 20 + dd, z); return [p[0] + t[0] * 11 * s, p[1] + t[1] * 11 * s, z]; };
    scene.prism(W, [col(-11, -1, BACKWALL.top), col(11, -1, BACKWALL.top), col(11, 1, BACKWALL.top), col(-11, 1, BACKWALL.top)],
      [col(-11, -1, cz), col(11, -1, cz), col(11, 1, cz), col(-11, 1, cz)], 64, {}, ["top", "bottom"]);
    // facade fin
    const fz = roofZ(i, 0.03) - ROOF.deck, fin = (dd, s, z) => { const p = P(i, BACKWALL.d1 + dd, z); return [p[0] + t[0] * 7 * s, p[1] + t[1] * 7 * s, z]; };
    scene.prism(M("fin"), [fin(0, -1, FLOOR), fin(56, -1, FLOOR), fin(56, 1, FLOOR), fin(0, 1, FLOOR)], [fin(0, -1, fz), fin(34, -1, fz), fin(34, 1, fz), fin(0, 1, fz)], 128, {}, ["bottom", "s3"]);
  }

  // ---- video walls over both ends ---------------------------------------------------------------
  const screens = [];
  for (const [i, team] of [[N / 4, "north"], [3 * N / 4, "south"]]) {
    const s = station(i), t = tangent(i), n = [s.nx, s.ny, 0], V = VIDEO_WALL, a = V.tilt * Math.PI / 180;
    const upv = vec.norm([-n[0] * Math.sin(a), -n[1] * Math.sin(a), Math.cos(a)]), fwd = vec.norm([-n[0] * Math.cos(a), -n[1] * Math.cos(a), -Math.sin(a)]);
    const top = roofZ(i, 1) - V.drop, c = vec.add(P(i, V.d, top - V.height / 2 * Math.cos(a)), [0, 0, 0]);
    const corner = (sr, su, sf) => vec.add(vec.add(vec.add(c, vec.mul(t, sr * V.width / 2)), vec.mul(upv, su * V.height / 2)), vec.mul(fwd, sf * V.depth / 2));
    // front: the viewer's left is the +tangent side
    scene.quad(M("videowall"), corner(1, -1, 1), corner(-1, -1, 1), corner(-1, 1, 1), corner(1, 1, 1), [[0, 1], [1, 1], [1, 0], [0, 0]], fwd, {});
    const D = M("steel_dark");
    scene.prism(D, [corner(1, -1, -1), corner(-1, -1, -1), corner(-1, -1, 1), corner(1, -1, 1)], [corner(1, 1, -1), corner(-1, 1, -1), corner(-1, 1, 1), corner(1, 1, 1)], 128, {}, ["s2"]);
    // frame lip round the picture
    for (const [sr, su] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) {
      const lip = (r, u, f) => vec.add(vec.add(vec.add(c, vec.mul(t, r)), vec.mul(upv, u)), vec.mul(fwd, f));
      const w2 = V.width / 2, h2 = V.height / 2, e = 8, f0 = V.depth / 2, f1 = V.depth / 2 + 5;
      const [r0, r1, u0, u1] = sr ? [sr * w2, sr * (w2 + e), -h2 - e, h2 + e] : [-w2 - e, w2 + e, su * h2, su * (h2 + e)];
      scene.prism(D, [lip(r0, u0, f0), lip(r1, u0, f0), lip(r1, u1, f0), lip(r0, u1, f0)], [lip(r0, u0, f1), lip(r1, u0, f1), lip(r1, u1, f1), lip(r0, u1, f1)], 128, {}, ["bottom"]);
    }
    // hangers up to the roof edge beam
    for (const sr of [-0.62, 0.62]) {
      const base = vec.add(vec.add(c, vec.mul(t, sr * V.width / 2)), vec.mul(upv, V.height / 2));
      const hz = roofZ(i, 1) - 32, h = (dx, dy, z) => [base[0] + t[0] * dx - n[0] * dy, base[1] + t[1] * dx - n[1] * dy, z];
      scene.prism(W, [h(-7, -7, base[2] - 6), h(7, -7, base[2] - 6), h(7, 7, base[2] - 6), h(-7, 7, base[2] - 6)], [h(-7, -7, hz), h(7, -7, hz), h(7, 7, hz), h(-7, 7, hz)], 64, {}, ["top", "bottom"]);
    }
    const face = vec.add(c, vec.mul(fwd, V.depth / 2));
    screens.push({ end: team, centre: face, normal: fwd, right: vec.mul(t, -1), up: upv, width: V.width, height: V.height,
      // middle of the two score boxes of the picture (the plugin's numbers sit scoreX left and right of it)
      score: vec.add(face, vec.mul(upv, V.height * (0.5 - V.scoreV))), scoreX: V.scoreX });
  }

  // ---- tunnel heads at the middle of both long sides --------------------------------------------
  const tunnels = [];
  for (const [i0, name] of [[0, "east"], [N / 2, "west"]]) {
    const dF = -14, dB = lowerRow(3).d0, zT = 96, open = 80 / TUNNEL_HALF, door = name === "west";
    const pt = (w, d, z) => P(i0 + w, d, z);   // w = -1..1 across the head
    const face = (material, w0, w1, z0, z1, d, facing, o = {}) => scene.quad(material, pt(w0, d, z0), pt(w1, d, z0), pt(w1, d, z1), pt(w0, d, z1),
      o.uv ?? [[-w0 * TUNNEL_HALF / 256, -z0 / 256], [-w1 * TUNNEL_HALF / 256, -z0 / 256], [-w1 * TUNNEL_HALF / 256, -z1 / 256], [-w0 * TUNNEL_HALF / 256, -z1 / 256]], facing, { solid: true, ...o });
    const inw = inward(i0);
    face(CW, -1, -open, FLOOR, zT, dF, inw); face(CW, open, 1, FLOOR, zT, dF, inw); face(M("tunnel_trim"), -open, open, 80, zT, dF, inw, { uv: [[1, 1], [0, 1], [0, 0], [1, 0]] });
    scene.quad(CW, pt(-1, dF, zT), pt(1, dF, zT), pt(1, dB, zT), pt(-1, dB, zT), [[0, 0], [208 / 256, 0], [208 / 256, 176 / 256], [0, 176 / 256]], UP, { solid: true });
    radial(CW, i0 - 1, dF, FLOOR, dB, zT, -1, { solid: true }); radial(CW, i0 + 1, dF, FLOOR, dB, zT, 1, { solid: true });
    if (door) {
      // the v8 door stays (shut) 2 to 10 units behind this opening
      const t = tangent(i0);
      for (const s of [-1, 1]) scene.quad(CW, pt(s * open, dF, FLOOR), pt(s * open, 0, FLOOR), pt(s * open, 0, 80), pt(s * open, dF, 80), [[0, 0.3], [0.06, 0.3], [0.06, 0], [0, 0]], vec.mul(t, -s), { solid: true });
      scene.quad(CW, pt(-open, dF, 80), pt(open, dF, 80), pt(open, 0, 80), pt(-open, 0, 80), [[0, 0], [0.6, 0], [0.6, 0.06], [0, 0.06]], DOWN, { solid: true });
      face(M("dark"), -open, open, FLOOR, 80, -1, inw);
    } else face(M("shutter"), -open, open, FLOOR, 80, dF, inw, { uv: [[1, 1], [0, 1], [0, 0], [1, 0]] });
    tunnels.push({ side: name, door, centre: pt(0, dF, FLOOR) });
  }

  dugouts(scene);

  return { scene, screens, tunnels, arc };
}

// ---- dugouts: where v8 had them (the plugin seats its substitutes on these benches) -------------
// x -1580 (back) .. -1500 (front); y 212..470 (red, north) and -473..-215 (blue, south);
// bench seat top z -13, hips at x -1558, six seats 38 units apart from |y| 248.
export const DUGOUTS = [{ team: "red", y0: 212, y1: 470 }, { team: "blue", y0: -473, y1: -215 }];
function dugouts(scene) {
  const D = M("steel_dark"), GL = M("glass_rail"), RG = M("roof_glass");
  const box = (material, x0, y0, z0, x1, y1, z1, o = {}, skip = []) => scene.prism(material, [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0]], [[x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]], 64, o, skip);
  for (const { team, y0, y1 } of DUGOUTS) {
    const xb = -1580, xf = -1500, sign = y0 > 0 ? 1 : -1, first = sign > 0 ? 248 : -438;
    // roof line from the back wall forward
    const roof = [[xb - 2, 62], [-1556, 73], [-1526, 71], [xf + 2, 58]];
    for (let k = 0; k + 1 < roof.length; k++) {
      const [xa, za] = roof[k], [xc, zc] = roof[k + 1];
      scene.quad(RG, [xa, y0 - 3, za], [xc, y0 - 3, zc], [xc, y1 + 3, zc], [xa, y1 + 3, za], [[k, 0], [k + 1, 0], [k + 1, (y1 - y0) / 64], [k, (y1 - y0) / 64]], UP, { twoSided: true });
    }
    // back: a solid panel, glass above; glass sides
    box(D, xb - 2, y0, FLOOR, xb, y1, -4, { solid: true }, ["bottom"]);
    scene.quad(GL, [xb - 1, y0, -4], [xb - 1, y1, -4], [xb - 1, y1, 62], [xb - 1, y0, 62], [[0, 1], [(y1 - y0) / 64, 1], [(y1 - y0) / 64, 0], [0, 0]], [1, 0, 0], { twoSided: true, solid: true });
    for (const y of [y0, y1]) scene.quad(GL, [xb, y, FLOOR], [-1512, y, FLOOR], [-1512, y, 66], [xb, y, 62], [[0, 1], [1, 1], [1, 0], [0, 0]], [0, y === y0 ? -1 : 1, 0], { twoSided: true, solid: true });
    // frame
    for (const y of [y0, y1]) { box(D, xb - 2, y - 1.5, FLOOR, xb + 1, y + 1.5, 63, {}, ["bottom"]); box(D, -1513.5, y - 1.5, FLOOR, -1510.5, y + 1.5, 67, {}, ["bottom"]); }
    box(D, xf, y0 - 3, 55, xf + 3, y1 + 3, 59, {}, []); box(D, xb - 3, y0 - 3, 60, xb, y1 + 3, 64, {}, []);
    // bench: a slab on legs, six shell seats in the team colour
    box(D, -1570, y0 + 14, -17, -1545, y1 - 14, -13, { solid: true }, []);
    for (let y = y0 + 20; y <= y1 - 20; y += (y1 - y0 - 40) / 5) box(D, -1562, y - 2, FLOOR, -1553, y + 2, -17, {}, ["bottom", "top"]);
    const seat = M(`seat_${team}`), ya = first - 19, yb = first + 5 * 38 + 19;
    scene.quad(seat, [-1568.5, ya, -12], [-1568.5, yb, -12], [-1572, yb, 13], [-1572, ya, 13], [[0, 0.5], [6, 0.5], [6, 0], [0, 0]], [1, 0, 0.2], { twoSided: true });
    scene.quad(seat, [-1567, ya, -12.6], [-1547, ya, -12.6], [-1547, yb, -12.6], [-1567, yb, -12.6], [[0, 0.5], [0, 1], [6, 1], [6, 0.5]], UP, { twoSided: true });
  }
}

// ---- seats: rows of tip-up seats as three cut-out strips (support, folded seat, backrest) -------
const FONT = {
  S: ["01111", "10000", "10000", "01110", "00001", "00001", "11110"], O: ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
  C: ["01111", "10000", "10000", "10000", "10000", "10000", "01111"], E: ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
  R: ["11110", "10001", "10001", "11110", "10100", "10010", "10001"], M: ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
  D: ["11110", "10001", "10001", "10001", "10001", "10001", "11110"], A: ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
  N: ["10001", "11001", "10101", "10011", "10001", "10001", "10001"],
};
const WORDS = { east: "ARENA", west: "SOCCERMOD" };
const PX = 44, TEXT_ROW0 = 3, TEXT_ROWS_PER_PX = 2;
// colour of one seat: tier 0 lower / 1 upper, row, S = position on the front curve, n = station normal there
export function seatColour(tier, row, S, nx, ny) {
  const end = ny * ny, team = ny > 0 ? "red" : "blue";
  if (tier === 0 && (row === 7 || row === 8)) return "white";
  if (tier === 1 && end < 0.2) {
    const side = nx > 0 ? "east" : "west", word = WORDS[side], cols = word.length * 6 - 1;
    const mid = side === "east" ? 0 : RING.perimeter / 2;
    let rel = S - mid; if (rel > RING.perimeter / 2) rel -= RING.perimeter; if (rel < -RING.perimeter / 2) rel += RING.perimeter;
    const col = Math.floor((cols * PX / 2 - rel) / PX), line = 6 - Math.floor((row - TEXT_ROW0) / TEXT_ROWS_PER_PX);
    if (col >= 0 && col < cols && row >= TEXT_ROW0 && line >= 0 && line <= 6 && col % 6 < 5 && FONT[word[Math.floor(col / 6)]][line][col % 6] === "1") return "white";
  }
  // sides graphite, ends in the team colour, the corners fade in diagonal stripes
  const p = Math.min(1, Math.max(0, (end - 0.28) / 0.44));
  const stripe = ((Math.floor(S / SEAT.width) + row * 2) % 12 + 12) % 12 / 12;
  return p > stripe + 0.04 ? team : "dark";
}

function seats(scene, arc) {
  const mat = (c) => M(`seat_${c}`);
  for (const [tier, rows, rowFn, firstRow] of [[0, LOWER.rows, lowerRow, () => 0], [1, UPPER.rows, upperRow, () => 0]]) {
    for (let b = 0; b < blocks.length; b++) {
      const segs = blocks[b];
      for (let k = firstRow(); k < rows; k++) {
        const r = rowFn(k), dSeat = r.d1 - 12;
        // the tunnel heads cover the first rows of the lower stand
        const usable = segs.filter((i) => !(tier === 0 && k < 3 && kinds[i] === "tunnel"));
        if (!usable.length) continue;
        // runs of neighbouring segments
        const runs = [];
        for (const i of usable) { const last = runs.at(-1); if (last && mod(last.at(-1) + 1) === i) last.push(last.at(-1) + 1); else runs.push([i]); }
        for (const run of runs) {
          const A0 = arc(dSeat), segLen = (i) => A0[mod(i) + 1] - A0[mod(i)], len = run.reduce((s, i) => s + segLen(i), 0), margin = 7;
          const count = Math.max(1, Math.round((len - 2 * margin) / SEAT.width)), pitch = (len - 2 * margin) / count;
          // position along the run -> fractional station
          const at = (x) => { let acc = 0; for (const i of run) { const l = segLen(i); if (x <= acc + l + 1e-9) return i + Math.min(1, Math.max(0, (x - acc) / l)); acc += l; } return run.at(-1) + 1; };
          const colours = [];
          for (let s = 0; s < count; s++) {
            const f = at(margin + (s + 0.5) * pitch), i0 = Math.floor(f), t = f - i0, a = station(i0), bst = station(i0 + 1);
            const S = a.S + ((mod(i0) + 1 >= N ? RING.perimeter : bst.S) - a.S) * t;
            const nx = a.nx + (bst.nx - a.nx) * t, ny = a.ny + (bst.ny - a.ny) * t, nl = Math.hypot(nx, ny);
            colours.push(seatColour(tier, k, S, nx / nl, ny / nl));
          }
          // strips of equal colour, cut at the station lines
          for (let s0 = 0; s0 < count;) {
            let s1 = s0 + 1; while (s1 < count && colours[s1] === colours[s0]) s1++;
            const x0 = margin + s0 * pitch, x1 = margin + s1 * pitch, f0 = at(x0), f1 = at(x1);
            const cuts = [f0]; for (let c = Math.floor(f0) + 1; c < f1 - 1e-9; c++) cuts.push(c); cuts.push(f1);
            let acc = 0; const starts = new Map(); for (const i of run) { starts.set(i, acc); acc += segLen(i); }
            const xOf = (f) => { const i = Math.min(run.at(-1), Math.floor(f - 1e-12)); return starts.get(i) + (f - i) * segLen(i); };
            for (let c = 0; c + 1 < cuts.length; c++) {
              const fa = cuts[c], fb = cuts[c + 1]; if (fb - fa < 1e-9) continue;
              const ua = (xOf(fa) - margin) / pitch, ub = (xOf(fb) - margin) / pitch;
              const strip = (material, da, za, db, zb, va, vb) => {
                const pts = [P(fa, da, r.z + za), P(fb, da, r.z + za), P(fb, db, r.z + zb), P(fa, db, r.z + zb)];
                const i = Math.floor(fa), s = station(i), inw = [-s.nx, -s.ny, 0.2];
                scene.quad(material, ...pts, [[-ua, va], [-ub, va], [-ub, vb], [-ua, vb]], inw, { twoSided: true });
              };
              strip(M("seat_frame"), dSeat, 0, dSeat, 14, 1, 0);
              strip(mat(colours[s0]), dSeat - 3, 10, dSeat - 1, 29, 1, 0.5);
              strip(mat(colours[s0]), dSeat, 14, dSeat + 6, 35, 0.5, 0);
            }
            s0 = s1;
          }
        }
      }
    }
  }
}
