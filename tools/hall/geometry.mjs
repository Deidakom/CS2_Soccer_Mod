// SoccerMod indoor hall: every face of the map, grouped by material and by model (opts.group).
// layout.mjs holds the numbers. Collision is a handful of plain thick boxes (physOnly: in the
// collision mesh only), so the ball and the players meet simple, solid shapes.
import {
  FLOOR, Z, PITCH, BOARD, GLASS, NET, GOAL, HALL, WALK, WEST, EAST, END, LOUNGE, SEAT, westRow, eastRow, endStep,
  WEST_TOP, EAST_TOP, END_TOP, ROOF_SEGMENTS, roofH, TRUSS, LIGHTS, CUBE, boardLine, pitchLines, clipConvex, DESIGNS, designCells,
} from "./layout.mjs";
import { Scene } from "../arena/lib/mesh.mjs";

const UP = [0, 0, 1], DOWN = [0, 0, -1], PX = [1, 0, 0], NX = [-1, 0, 0], PY = [0, 1, 0], NY = [0, -1, 0];
const M = (name) => `materials/soccermod_hall/${name}.vmat`;
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];

export function buildHall() {
  const scene = new Scene();
  const crowd = [];     // rows of fans: { a: [x, y], b: [x, y], h, look: [nx, ny], group: 0 red | 1 blue | 2 mixed, name }
  const lights = [];    // baked lights: { at: [x, y, z], brightness, range, color? }
  let G = "shell";      // the model the next faces go into
  const O = (o = {}) => ({ group: G, ...o });

  // ---- helpers -------------------------------------------------------------------------------------
  // box from two corners; heights are above the floor. skip: "x0" "x1" "y0" "y1" "top" "bottom"
  const SIDE = { s0: "y0", s1: "x1", s2: "y1", s3: "x0", top: "top", bottom: "bottom" };
  const box = (mat, x0, y0, h0, x1, y1, h1, o = {}, skip = [], uv = 64) => {
    const b = [[x0, y0, Z(h0)], [x1, y0, Z(h0)], [x1, y1, Z(h0)], [x0, y1, Z(h0)]], t = b.map((p) => [p[0], p[1], Z(h1)]);
    scene.prism(M(mat), b, t, uv, O(o), Object.keys(SIDE).filter((k) => skip.includes(SIDE[k])));
  };
  // collision only: a solid box that is not drawn
  const solid = (x0, y0, h0, x1, y1, h1) => box("collide", x0, y0, h0, x1, y1, h1, { solid: true, physOnly: true });
  // a rectangle: origin o (world), edge u (along the texture's u), edge v (towards the texture's top)
  const rect = (mat, o, u, v, [u0, v0, u1, v1], facing, opts = {}) =>
    scene.quad(M(mat), o, add(o, u), add(add(o, u), v), add(o, v), [[u0, v1], [u1, v1], [u1, v0], [u0, v0]], facing, O(opts));
  const flat = (mat, x0, y0, x1, y1, h, tile, facing = UP, opts = {}) =>
    scene.quad(M(mat), [x0, y0, Z(h)], [x1, y0, Z(h)], [x1, y1, Z(h)], [x0, y1, Z(h)], [[x0 / tile, -y0 / tile], [x1 / tile, -y0 / tile], [x1 / tile, -y1 / tile], [x0 / tile, -y1 / tile]], facing, O(opts));
  // vertical wall on a line of constant x (looking along +-x) or constant y; u runs along the wall
  const wallX = (mat, x, y0, y1, h0, h1, facing, uv, opts = {}) => {
    const [u0, v0, u1, v1] = uv ?? [y0 / 128, 0, y1 / 128, (h1 - h0) / 128];
    scene.quad(M(mat), [x, y0, Z(h0)], [x, y1, Z(h0)], [x, y1, Z(h1)], [x, y0, Z(h1)], [[u0, v1], [u1, v1], [u1, v0], [u0, v0]], facing, O(opts));
  };
  const wallY = (mat, y, x0, x1, h0, h1, facing, uv, opts = {}) => {
    const [u0, v0, u1, v1] = uv ?? [x0 / 128, 0, x1 / 128, (h1 - h0) / 128];
    scene.quad(M(mat), [x0, y, Z(h0)], [x1, y, Z(h0)], [x1, y, Z(h1)], [x0, y, Z(h1)], [[u0, v1], [u1, v1], [u1, v0], [u0, v0]], facing, O(opts));
  };
  // clip a convex polygon (list of [x, y]) to y0 <= y <= y1
  const clipY = (poly, y0, y1) => {
    const cut = (pts, keep, at) => {
      const out = [];
      for (let i = 0; i < pts.length; i++) {
        const a = pts[i], b = pts[(i + 1) % pts.length], ka = keep(a), kb = keep(b);
        if (ka) out.push(a);
        if (ka !== kb) { const t = (at - a[1]) / (b[1] - a[1]); out.push([a[0] + (b[0] - a[0]) * t, at]); }
      }
      return out;
    };
    const out = cut(cut(poly, (p) => p[1] >= y0, y0), (p) => p[1] <= y1, y1);
    // a vertex exactly on a band edge comes out twice; the model compiler drops a face with a repeated
    // point (two turf bands at the halfway line were missing in the game, owner 2026-10-01)
    return out.filter((p, i) => { const q = out[(i + out.length - 1) % out.length]; return Math.hypot(p[0] - q[0], p[1] - q[1]) > 1e-4; });
  };

  const L0 = boardLine(0), N = L0.pts.length, at = (line, i) => line.pts[((i % N) + N) % N];
  const lineCache = new Map();
  const line = (d) => { if (!lineCache.has(d)) lineCache.set(d, boardLine(d)); return lineCache.get(d); };
  // a band along the board line at offset d between two heights; u from the path length
  const band = (mat, d, h0, h1, { tileU = 128, v = null, facing = "in", goal = false, opts = {} } = {}) => {
    const Ld = line(d);
    for (let i = 0; i < N; i++) {
      const a0 = at(L0, i); if (a0.goal && !goal) continue;
      const a = at(Ld, i), b = at(Ld, i + 1), flip = facing === "in" ? -1 : 1, u0 = flip * a0.S / tileU, u1 = flip * (a0.S + a0.len) / tileU, [v0, v1] = v ?? [0, (h1 - h0) / tileU];
      const f = facing === "in" ? [-a0.nx, -a0.ny, 0] : [a0.nx, a0.ny, 0];   // seen from inside the path runs right to left: u is mirrored
      scene.quad(M(mat), [a.x, a.y, Z(h0)], [b.x, b.y, Z(h0)], [b.x, b.y, Z(h1)], [a.x, a.y, Z(h1)], [[u0, v1], [u1, v1], [u1, v0], [u0, v0]], f, O(opts));
    }
  };
  // a level strip between two offsets
  const cap = (mat, d0, d1, h, facing = UP, { goal = false, tile = 64, opts = {} } = {}) => {
    const A = line(d0), B = line(d1);
    for (let i = 0; i < N; i++) {
      const a0 = at(L0, i); if (a0.goal && !goal) continue;
      const a = at(A, i), b = at(A, i + 1), c = at(B, i + 1), d = at(B, i), u0 = a0.S / tile, u1 = (a0.S + a0.len) / tile;
      scene.quad(M(mat), [a.x, a.y, Z(h)], [b.x, b.y, Z(h)], [c.x, c.y, Z(h)], [d.x, d.y, Z(h)], [[u0, 0], [u1, 0], [u1, 1], [u0, 1]], facing, O(opts));
    }
  };

  // =================================================================================================
  // the pitch: turf in two tones (bands across the length), white lines, the turf inside the goals
  // =================================================================================================
  G = "pitch";
  {
    const outline = L0.pts.map((p) => [p.x, p.y]), BANDS = 20, bandH = (2 * PITCH.gy) / BANDS, TILE = 170.6667;
    for (let k = 0; k < BANDS; k++) {
      const y0 = -PITCH.gy + k * bandH, poly = clipY(outline, y0, y0 + bandH);
      // as triangles from the first corner, so nothing is left to the model compiler
      for (let i = 1; i + 1 < poly.length; i++) { const tri = [poly[0], poly[i], poly[i + 1]]; scene.poly(M(k % 2 ? "turf_b" : "turf_a"), tri.map(([x, y]) => [x, y, Z(0)]), tri.map(([x, y]) => [x / TILE, -y / TILE]), UP, O()); }
    }
    for (const s of [1, -1]) {
      const y0 = s * PITCH.gy, y1 = s * (PITCH.gy + GOAL.housingDepth - GOAL.wall), c = GOAL.housingHalf - GOAL.wall;
      flat("turf_a", -c, Math.min(y0, y1), c, Math.max(y0, y1), 0, TILE);
    }
    // lines, half a unit above the turf
    for (const line of pitchLines()) scene.poly(M("line"), line.map(([x, y]) => [x, y, Z(0.5)]), line.map(([x, y]) => [x / 32, -y / 32]), UP, O());
    // collision: the floor of the whole hall, turf surface
    solid(HALL.x0, -HALL.y, -64, HALL.x1, HALL.y, 0);
  }

  // =================================================================================================
  // pitch designs: one floor per design for the plugin (prop_dynamic at the floor + 0.4, chosen per
  // player). Model coordinates: z 0 = that height; the lines lie 0.35 above it, over the map's own.
  // =================================================================================================
  {
    const outline = L0.pts.map((p) => [p.x, p.y]), TILE = 170.6667;
    for (const name of DESIGNS) {
      G = `design_${name}`;
      for (const { tone, poly } of designCells(name)) {
        const cut = clipConvex(poly, outline);
        for (let i = 1; i + 1 < cut.length; i++) { const tri = [cut[0], cut[i], cut[i + 1]]; scene.poly(M(tone ? "turf_b" : "turf_a"), tri.map(([x, y]) => [x, y, 0]), tri.map(([x, y]) => [x / TILE, -y / TILE]), UP, O()); }
      }
      for (const line of pitchLines()) scene.poly(M("line"), line.map(([x, y]) => [x, y, 0.35]), line.map(([x, y]) => [x / 32, -y / 32]), UP, O());
    }
  }

  // =================================================================================================
  // the court: boards with LED adverts, acrylic panels, nets, the goals
  // =================================================================================================
  G = "court";
  {
    const AD = BOARD.h * 16;   // the advert strip is 16 : 1
    band("board_led", 0, 0, BOARD.h, { tileU: AD, v: [0, 1], facing: "in" });
    band("board_back", BOARD.t, 0, BOARD.h, { facing: "out" });
    band("board_cap", -1, BOARD.h, BOARD.h + BOARD.capH, { tileU: 32, v: [0, 0.5], facing: "in" });
    band("board_cap", BOARD.t + 1, BOARD.h, BOARD.h + BOARD.capH, { tileU: 32, v: [0, 0.5], facing: "out" });
    cap("board_cap", -1, BOARD.t + 1, BOARD.h + BOARD.capH, UP, { tile: 32 });
    // acrylic panels and nets
    band("glass", GLASS.d, GLASS.h0, GLASS.h1, { tileU: 168, v: [0, 1], facing: "in", opts: { twoSided: true } });
    band("net", NET.d, GLASS.h1, NET.h1, { tileU: 128, facing: "in", opts: { twoSided: true } });
    const top = line(NET.d).pts.map((p) => [p.x, p.y, Z(NET.h1)]);
    scene.poly(M("net_top"), top, top.map((p) => [p[0] / 128, -p[1] / 128]), DOWN, O({ twoSided: true }));
    // posts for panels and nets, a top rail
    for (let i = 0; i < N; i++) {
      const p = at(line(NET.d + 2), i), a0 = at(L0, i);
      if (!p.post || (a0.goal && Math.abs(p.x) < GOAL.housingHalf - 1)) continue;
      box("steel_dark", p.x - 2.5, p.y - 2.5, BOARD.h + BOARD.capH, p.x + 2.5, p.y + 2.5, NET.h1 + 6, {}, ["bottom"]);
    }
    band("steel_dark", NET.d + 1, GLASS.h1 - 2, GLASS.h1 + 2, { facing: "in", goal: true });
    band("steel_dark", NET.d + 1, NET.h1 - 3, NET.h1 + 3, { facing: "in", goal: true });
    // collision: one thick wall round the court from the floor to above the ceiling net, a lid on top
    const Lo = line(40), TOP = NET.h1 + 40;
    for (let i = 0; i < N; i++) {
      const a0 = at(L0, i); if (a0.goal) continue;
      const a = at(L0, i), b = at(L0, i + 1), c = at(Lo, i + 1), d = at(Lo, i);
      scene.prism(M("collide"), [[a.x, a.y, Z(0)], [b.x, b.y, Z(0)], [c.x, c.y, Z(0)], [d.x, d.y, Z(0)]], [[a.x, a.y, Z(TOP)], [b.x, b.y, Z(TOP)], [c.x, c.y, Z(TOP)], [d.x, d.y, Z(TOP)]], 64, O({ solid: true, physOnly: true }));
    }
    solid(-PITCH.hx - 40, -PITCH.gy - 40, NET.h1, PITCH.hx + 40, PITCH.gy + 40, TOP);

    // the goals
    for (const s of [1, -1]) {
      const team = s > 0 ? "red" : "blue", gy = s * PITCH.gy, H = GOAL.housingHalf, c = H - GOAL.wall, back = gy + s * GOAL.housingDepth, cb = gy + s * (GOAL.housingDepth - GOAL.wall);
      const ch = GOAL.housingH - GOAL.wall, P = GOAL.post, mouth = GOAL.half + P, bar = GOAL.height + P, look = s > 0 ? NY : PY, away = s > 0 ? PY : NY;
      const Y = (a, b) => [Math.min(a, b), Math.max(a, b)];
      // posts and crossbar, centred on the goal line
      for (const x of [-1, 1]) box("post_white", x > 0 ? GOAL.half : -mouth, gy - P / 2, 0, x > 0 ? mouth : -GOAL.half, gy + P / 2, bar, { solid: true }, ["bottom"]);
      box("post_white", -GOAL.half, gy - P / 2, GOAL.height, GOAL.half, gy + P / 2, bar, { solid: true }, ["x0", "x1"]);
      // the housing's front: dark panels beside and above the mouth, a lit trim round it
      wallY("goal_face", gy, -H, -mouth, 0, GOAL.housingH, look); wallY("goal_face", gy, mouth, H, 0, GOAL.housingH, look);
      wallY("goal_face", gy, -mouth, mouth, bar, GOAL.housingH, look);
      const trim = `goal_trim_${team}`, ty = gy - s * 0.6;
      wallY(trim, ty, -mouth - 8, -mouth - 3, 0, bar + 8, look, [0, 0, 1, 1]); wallY(trim, ty, mouth + 3, mouth + 8, 0, bar + 8, look, [0, 0, 1, 1]);
      wallY(trim, ty, -mouth - 8, mouth + 8, bar + 3, bar + 8, look, [0, 0, 1, 1]);
      wallY(trim, ty, -H, H, GOAL.housingH - 6, GOAL.housingH, look, [0, 0, 1, 1]);
      // inside: padded walls and ceiling
      const [yA, yB] = Y(gy, cb);
      wallX("goal_pad", -c, yA, yB, 0, ch, PX); wallX("goal_pad", c, yA, yB, 0, ch, NX);
      wallY("goal_pad", cb, -c, c, 0, ch, look);
      flat("goal_pad", -c, yA, c, yB, ch, 64, DOWN);
      // outside: the housing as seen from the terrace, with a lit edge
      const [oA, oB] = Y(gy + s * BOARD.t, back);
      wallX("goal_shell", -H, oA, oB, 0, GOAL.housingH, NX); wallX("goal_shell", H, oA, oB, 0, GOAL.housingH, PX);
      wallY("goal_shell", back, -H, H, 0, GOAL.housingH, away);
      flat("goal_shell", -H, oA, H, oB, GOAL.housingH, 64, UP);
      wallY(trim, back + s * 0.6, -H, H, GOAL.housingH - 8, GOAL.housingH - 2, away, [0, 0, 1, 1]);
      // collision: the front beside and above the mouth, the walls, back and ceiling of the housing
      const [fA, fB] = Y(gy, gy + s * 16), [bA, bB] = Y(cb, cb + s * 40);
      solid(-H - 40, fA, 0, -mouth, fB, NET.h1 + 40); solid(mouth, fA, 0, H + 40, fB, NET.h1 + 40); solid(-mouth, fA, bar, mouth, fB, NET.h1 + 40);
      solid(-c - 40, yA, 0, -c, yB, ch + 40); solid(c, yA, 0, c + 40, yB, ch + 40);
      solid(-c - 40, bA, 0, c + 40, bB, ch + 40); solid(-c - 40, Math.min(yA, bA), ch, c + 40, Math.max(yB, bB), ch + 40);
    }
  }

  // =================================================================================================
  // floors and the hall's walls
  // =================================================================================================
  G = "shell";
  {
    const { x0, x1, y, eave } = HALL;
    flat("walkway", x0, -y, x1, y, -0.6, 128);                      // under everything; the turf lies 0.6 above
    // long walls
    wallX("wall_base", x0, -y, y, 0, WEST_TOP.h, PX);
    wallX("wall_dark", x0, -y, y, LOUNGE.h1, eave, PX);
    wallX("wall_timber", x1, -y, y, 0, eave, NX, [-y / 256, 0, y / 256, eave / 256]);
    // east wall: the hall's name as a mural, a band of windows under the eave
    wallX("mural", x1 - 1.5, -520, 520, 205, 465, NX, [1, 0, 0, 1]);
    wallX("windows", x1 - 1.5, -y + 60, y - 60, 478, 530, NX, [0, 0, (2 * y - 120) / 208, 1]);
    // end walls with the gable up to the roof
    for (const s of [1, -1]) {
      const look = s > 0 ? NY : PY, wy = s * y;
      wallY("wall_dark", wy, x0, x1, 0, eave, look);
      for (let j = 0; j < ROOF_SEGMENTS; j++) {
        const xa = x0 + (x1 - x0) * j / ROOF_SEGMENTS, xb = x0 + (x1 - x0) * (j + 1) / ROOF_SEGMENTS;
        scene.poly(M("wall_dark"), [[xa, wy, Z(eave)], [xb, wy, Z(eave)], [xb, wy, Z(roofH(xb))], [xa, wy, Z(roofH(xa))]],
          [[xa / 128, 0], [xb / 128, 0], [xb / 128, -(roofH(xb) - eave) / 128], [xa / 128, -(roofH(xa) - eave) / 128]], look, O());
      }
      // the big screen and the team banners
      const sy = wy - s * 6;
      wallY("video", sy, -330, 330, 262, 502, look, s > 0 ? [0, 0, 1, 1] : [1, 0, 0, 1]);
      box("steel_dark", -338, Math.min(sy, wy), 254, 338, Math.max(sy, wy), 510, {}, [s > 0 ? "y0" : "y1"]);
      const ban = `banner_${s > 0 ? "red" : "blue"}`;
      for (const bx of [-760, -560, 560, 760]) wallY(ban, wy - s * 3, bx - 60, bx + 60, 250, 500, look, s > 0 ? [0, 0, 1, 1] : [1, 0, 0, 1], { twoSided: true });
      // LED lines along the wall
      wallY(`goal_trim_${s > 0 ? "red" : "blue"}`, wy - s * 1.5, x0, x1, 226, 232, look, [0, 0, 1, 1]);
    }
  }

  // =================================================================================================
  // stands: seats on the long sides, standing terraces behind the goals
  // =================================================================================================
  const seatRows = (name, side, rowAt, rows, yEnd, aisles, aisleW, topX, topH, group) => {
    const sx = side;   // -1 west, +1 east: the direction the stand rises in
    const U = (a, b) => (sx < 0 ? [a, 0, b, 1] : [b, 0, a, 1]);   // pictures read left to right from the pitch
    const spans = []; { let y = -yEnd; for (const a of [...aisles, null]) { const end = a === null ? yEnd : a - aisleW / 2; if (end > y) spans.push([y, end]); y = a === null ? yEnd : a + aisleW / 2; } }
    for (let k = 0; k < rows; k++) {
      const r = rowAt(k), xa = Math.min(r.x0, r.x1), xb = Math.max(r.x0, r.x1);
      flat("concrete", xa, -yEnd, xb, yEnd, r.h, 96);
      if (k) wallX("riser", r.x0, -yEnd, yEnd, r.h - (rowAt(1).h - rowAt(0).h), r.h, sx < 0 ? PX : NX, [-yEnd / 64, 0, yEnd / 64, 1]);
      for (const a of aisles) {   // stairs: two steps per row, yellow nosing
        const half = (rowAt(1).h - rowAt(0).h) / 2, mid = (r.x0 + r.x1) / 2;
        box("stair", Math.min(r.x0, mid), a - aisleW / 2, r.h, Math.max(r.x0, mid), a + aisleW / 2, r.h + 0.4, {}, ["bottom", "x0", "x1", "y0", "y1"], 32);
        box("stair", Math.min(mid, r.x1), a - aisleW / 2, r.h, Math.max(mid, r.x1), a + aisleW / 2, r.h + half, {}, ["bottom", sx < 0 ? "x0" : "x1"], 32);
      }
      for (const [ya, yb] of spans) {
        const n = Math.floor((yb - ya - 8) / SEAT.width), lead = (yb - ya - n * SEAT.width) / 2, seatX = r.x1 - sx * 7, colour = k >= rows - 2 ? "seat_dark" : (Math.floor((ya + yEnd) / 500) + k) % 3 === 0 ? "seat_white" : name === "west" ? "seat_red" : "seat_blue";
        wallX(colour, seatX, ya + lead, yb - lead, r.h + 5, r.h + 33, sx < 0 ? PX : NX, U(0, n), { twoSided: true });
        wallX("seat_frame", seatX + sx * 1, ya + lead, yb - lead, r.h, r.h + 12, sx < 0 ? PX : NX, [0, 0, n, 1], { twoSided: true });
        crowd.push({ a: [(r.x0 + r.x1) / 2 - sx * 2, ya + lead], b: [(r.x0 + r.x1) / 2 - sx * 2, yb - lead], h: r.h, look: [-sx, 0], group, name });
      }
    }
    // front wall with printed adverts and a glass rail, the ends of the stand
    const front = rowAt(0).x0, h0 = rowAt(0).h;
    wallX("ads", front, -yEnd, yEnd, 0, h0, sx < 0 ? PX : NX, U(0, (2 * yEnd) / (h0 * 16)));
    wallX("rail_glass", front + sx * 1, -yEnd, yEnd, h0, h0 + 30, sx < 0 ? PX : NX, [0, 0, (2 * yEnd) / 64, 1], { twoSided: true });
    for (const e of [-1, 1]) {
      const pts = [[front, e * yEnd, Z(0)], [front, e * yEnd, Z(h0)]];
      for (let k = 0; k < rows; k++) { const r = rowAt(k); pts.push([r.x0, e * yEnd, Z(r.h)], [r.x1, e * yEnd, Z(r.h)]); }
      pts.push([topX, e * yEnd, Z(topH)], [topX, e * yEnd, Z(0)]);
      // a stair-shaped side: fan from the bottom corner under the back
      for (let k = 0; k + 1 < pts.length - 1; k++) scene.poly(M("wall_base"), [pts.at(-1), pts[k], pts[k + 1]], [pts.at(-1), pts[k], pts[k + 1]].map((p) => [p[0] / 128, -p[2] / 128]), e > 0 ? PY : NY, O({ twoSided: true }));
    }
  };

  G = "west";
  {
    seatRows("west", -1, westRow, WEST.rows, WEST.y, WEST.aisles, WEST.aisleW, WEST_TOP.x, WEST_TOP.h, 2);
    const { x0 } = HALL, cy = HALL.y - 40;
    // concourse behind the top row, over the whole length of the hall
    flat("walkway", x0, -cy, WEST_TOP.x, cy, WEST_TOP.h, 128);
    wallX("riser", WEST_TOP.x, -WEST.y, WEST.y, WEST_TOP.h - WEST.rise, WEST_TOP.h, PX, [-WEST.y / 64, 0, WEST.y / 64, 1]);
    for (const e of [-1, 1]) {   // beyond the seats the concourse ends in a wall down to the floor
      wallX("wall_base", WEST_TOP.x, Math.min(e * WEST.y, e * cy), Math.max(e * WEST.y, e * cy), 0, WEST_TOP.h, PX);
      wallX("rail_glass", WEST_TOP.x, Math.min(e * WEST.y, e * cy), Math.max(e * WEST.y, e * cy), WEST_TOP.h, WEST_TOP.h + 34, PX, [0, 0, (cy - WEST.y) / 64, 1], { twoSided: true });
      wallY("wall_base", e * cy, x0, WEST_TOP.x, 0, WEST_TOP.h + 34, e > 0 ? NY : PY);
    }
    // back wall of the concourse: bar, kiosks and doors as one long lit picture
    wallX("concourse", x0 + 1.5, -1024, 1024, WEST_TOP.h, WEST_TOP.h + 128, PX, [0, 0, 4, 1]);
    wallX("wall_timber", x0 + 1, -HALL.y, HALL.y, WEST_TOP.h + 128, LOUNGE.h0, PX, [-HALL.y / 256, 0, HALL.y / 256, (LOUNGE.h0 - WEST_TOP.h - 128) / 256]);
    // the lounge above it: glass front, a lit room behind, a light band on the edge
    const lx = x0 + LOUNGE.depth, ly = 900;
    flat("lounge_soffit", x0, -ly, lx, ly, LOUNGE.h0, 128, DOWN);
    wallX("fascia_led", lx, -ly, ly, LOUNGE.h0, LOUNGE.h0 + 22, PX, [0, 0, (2 * ly) / 64, 1]);
    wallX("lounge_interior", lx - 40, -ly, ly, LOUNGE.h0 + 22, LOUNGE.h1, PX, [0, 0, (2 * ly) / 450, 1]);
    wallX("lounge_glass", lx, -ly, ly, LOUNGE.h0 + 22, LOUNGE.h1, PX, [0, 0, (2 * ly) / 150, 1], { twoSided: true });
    flat("wall_dark", x0, -ly, lx, ly, LOUNGE.h1, 128, UP);
    for (const e of [-1, 1]) wallY("wall_dark", e * ly, x0, lx, LOUNGE.h0, LOUNGE.h1, e > 0 ? PY : NY, undefined, { twoSided: true });
    wallX("sign_lounge", lx + 1.2, -150, 150, LOUNGE.h1 - 2, LOUNGE.h1 + 38, PX, [0, 0, 1, 1], { twoSided: true });
    // standing fans on the concourse rail
    crowd.push({ a: [WEST_TOP.x - 26, -WEST.y + 20], b: [WEST_TOP.x - 26, WEST.y - 20], h: WEST_TOP.h, look: [1, 0], group: 2, name: "west", sparse: 0.45 });
  }

  G = "east";
  {
    seatRows("east", 1, eastRow, EAST.rows, EAST.y, EAST.aisles, EAST.aisleW, EAST_TOP.x, EAST_TOP.h, 2);
    const { x1 } = HALL, cy = HALL.y - 40;
    flat("walkway", EAST_TOP.x, -cy, x1, cy, EAST_TOP.h, 128);
    wallX("riser", EAST_TOP.x, -EAST.y, EAST.y, EAST_TOP.h - EAST.rise, EAST_TOP.h, NX, [-EAST.y / 64, 0, EAST.y / 64, 1]);
    for (const e of [-1, 1]) {
      wallX("wall_base", EAST_TOP.x, Math.min(e * EAST.y, e * cy), Math.max(e * EAST.y, e * cy), 0, EAST_TOP.h, NX);
      wallX("rail_glass", EAST_TOP.x, Math.min(e * EAST.y, e * cy), Math.max(e * EAST.y, e * cy), EAST_TOP.h, EAST_TOP.h + 34, NX, [0, 0, (cy - EAST.y) / 64, 1], { twoSided: true });
      wallY("wall_base", e * cy, EAST_TOP.x, x1, 0, EAST_TOP.h + 34, e > 0 ? NY : PY);
    }
    crowd.push({ a: [EAST_TOP.x + 26, -EAST.y + 20], b: [EAST_TOP.x + 26, EAST.y - 20], h: EAST_TOP.h, look: [-1, 0], group: 2, name: "east", sparse: 0.35 });
    // team shelters on the walkway in front of the east stand: bench, glass back and roof
    for (const s of [1, -1]) {
      const team = s > 0 ? "red" : "blue", ya = s > 0 ? 190 : -440, yb = ya + 250, bx0 = PITCH.hx + BOARD.t + 22, bx1 = bx0 + 60;
      box("steel_dark", bx0 + 22, ya + 8, 17, bx1 - 14, yb - 8, 21, {}, []);
      for (let y = ya + 20; y <= yb - 20; y += (yb - ya - 40) / 4) box("steel_dark", bx0 + 30, y - 2, 0, bx0 + 36, y + 2, 17, {}, ["bottom", "top"]);
      wallX(`seat_${team}`, bx1 - 15, ya + 12, yb - 12, 19, 47, NX, [0, 0, 6, 1], { twoSided: true });
      wallX("rail_glass", bx1, ya, yb, 0, 84, NX, [0, 0, 4, 1], { twoSided: true });
      scene.quad(M("rail_glass"), [bx1, ya, Z(84)], [bx1, yb, Z(84)], [bx0 - 6, yb, Z(72)], [bx0 - 6, ya, Z(72)], [[0, 0], [4, 0], [4, 1], [0, 1]], DOWN, O({ twoSided: true }));
      for (const y of [ya, yb]) { box("steel_dark", bx1 - 2, y - 1.5, 0, bx1 + 1, y + 1.5, 84, {}, ["bottom"]); wallY("rail_glass", y, bx0 + 4, bx1, 0, 78, y === ya ? PY : NY, [0, 0, 1, 1], { twoSided: true }); }
      wallX(`goal_trim_${team}`, bx1 - 0.6, ya, yb, 80, 84, NX, [0, 0, 1, 1]);
      crowd.push({ a: [bx1 - 22, ya + 30], b: [bx1 - 22, yb - 30], h: 0, look: [-1, 0], group: s > 0 ? 0 : 1, name: "east", bench: true, seatH: 21 });
      crowd.push({ a: [bx0 + 4, s > 0 ? ya - 40 : yb + 40], b: [bx0 + 4, s > 0 ? ya - 39 : yb + 41], h: 0, look: [-1, 0], group: s > 0 ? 0 : 1, name: "east", coach: true });
    }
  }

  G = "ends";
  for (const s of [1, -1]) {
    const team = s > 0 ? 0 : 1, look = s > 0 ? NY : PY, X = END.x;
    for (let k = 0; k < END.steps; k++) {
      const r = endStep(k), ya = s * r.y0, yb = s * r.y1;
      flat("concrete", -X, Math.min(ya, yb), X, Math.max(ya, yb), r.h, 96);
      wallY("riser", ya, -X, X, r.h - (k ? END.rise : r.h), r.h, look, [-X / 64, 0, X / 64, 1]);
      crowd.push({ a: [-X + 16, s * (r.y0 + END.depth * 0.45)], b: [X - 16, s * (r.y0 + END.depth * 0.45)], h: r.h, look: [0, -s], group: team, name: s > 0 ? "end_red" : "end_blue" });
      if (k % 2 === 1) for (let bx = -X + 90; bx < X; bx += 220) {   // crush barriers
        const by = s * (r.y0 + 6);
        box("steel_white", bx, by - 1.5, r.h + 36, bx + 120, by + 1.5, r.h + 39, {}, []);
        for (const px of [bx + 4, bx + 114]) box("steel_white", px, by - 1.5, r.h, px + 3, by + 1.5, r.h + 36, {}, ["bottom", "top"]);
      }
    }
    const top = s * END_TOP.y, wy = s * HALL.y;
    flat("walkway", -X, Math.min(top, wy), X, Math.max(top, wy), END_TOP.h, 128);
    wallY("riser", top, -X, X, END_TOP.h - END.rise, END_TOP.h, look, [-X / 64, 0, X / 64, 1]);
    for (const e of [-1, 1]) {   // side walls of the terrace
      const pts = [[e * X, s * END.y, Z(0)]];
      for (let k = 0; k < END.steps; k++) { const r = endStep(k); pts.push([e * X, s * r.y0, Z(r.h)], [e * X, s * r.y1, Z(r.h)]); }
      pts.push([e * X, top, Z(END_TOP.h)], [e * X, wy, Z(END_TOP.h)], [e * X, wy, Z(0)]);
      for (let k = 0; k + 1 < pts.length - 1; k++) scene.poly(M("wall_base"), [pts.at(-1), pts[k], pts[k + 1]], [pts.at(-1), pts[k], pts[k + 1]].map((p) => [p[1] / 128, -p[2] / 128]), e > 0 ? PX : NX, O({ twoSided: true }));
      wallX("rail_glass", e * X, Math.min(top, wy), Math.max(top, wy), END_TOP.h, END_TOP.h + 34, e > 0 ? PX : NX, [0, 0, 1.5, 1], { twoSided: true });
    }
    // light columns in the four corners of the court, in the end's colour
    for (const e of [-1, 1]) {
      const cx = e * (PITCH.hx - 18), cyy = s * (PITCH.gy - 18);
      box(`goal_trim_${s > 0 ? "red" : "blue"}`, cx - 9, cyy - 9, 0, cx + 9, cyy + 9, 300, {}, ["bottom"], 9999);
      box("steel_dark", cx - 13, cyy - 13, 300, cx + 13, cyy + 13, 312, {}, []);
    }
  }

  // =================================================================================================
  // roof: deck with a skylight along the ridge, trusses, purlins, light bars, the video cube
  // =================================================================================================
  G = "roof";
  {
    const { x0, x1, y } = HALL, xs = [...Array(ROOF_SEGMENTS + 1).keys()].map((j) => x0 + (x1 - x0) * j / ROOF_SEGMENTS);
    for (let j = 0; j < ROOF_SEGMENTS; j++) {
      const xa = xs[j], xb = xs[j + 1], sky = j === ROOF_SEGMENTS / 2 - 1 || j === ROOF_SEGMENTS / 2;
      const w = Math.hypot(xb - xa, roofH(xb) - roofH(xa));
      scene.quad(M(sky ? "skylight" : "roof_deck"), [xa, -y, Z(roofH(xa))], [xb, -y, Z(roofH(xb))], [xb, y, Z(roofH(xb))], [xa, y, Z(roofH(xa))],
        sky ? [[0, 0], [1, 0], [1, (2 * y) / 215], [0, (2 * y) / 215]] : [[0, 0], [w / 128, 0], [w / 128, (2 * y) / 128], [0, (2 * y) / 128]], DOWN, O());
    }
    for (const ty of TRUSS.y) for (let j = 0; j < ROOF_SEGMENTS; j++) {
      const xa = xs[j], xb = xs[j + 1], ta = roofH(xa) - 3, tb = roofH(xb) - 3;
      scene.quad(M("truss"), [xa, ty, Z(ta - TRUSS.depth)], [xb, ty, Z(tb - TRUSS.depth)], [xb, ty, Z(tb)], [xa, ty, Z(ta)], [[j / 2, 1], [(j + 1) / 2, 1], [(j + 1) / 2, 0], [j / 2, 0]], PY, O({ twoSided: true }));
    }
    for (let j = 1; j < ROOF_SEGMENTS; j++) box("steel_white", xs[j] - 4, -y, roofH(xs[j]) - 14, xs[j] + 4, y, roofH(xs[j]) - 3, {}, ["top", "y0", "y1"]);
    // LED light bars over the court, each on two rods
    for (const ly of LIGHTS.ys) for (const lx of LIGHTS.xs) {
      box("steel_dark", lx - 78, ly - 11, LIGHTS.h, lx + 78, ly + 11, LIGHTS.h + 9, {}, ["bottom"]);
      flat("led_bar", lx - 78, ly - 11, lx + 78, ly + 11, LIGHTS.h, 156, DOWN);
      for (const rx of [lx - 60, lx + 60]) box("steel_dark", rx - 0.8, ly - 0.8, LIGHTS.h + 9, rx + 0.8, ly + 0.8, roofH(rx) - 4, {}, ["top", "bottom"]);
      lights.push({ at: [lx, ly, Z(LIGHTS.h - 14)], brightness: 2.2, range: 2200 });
    }
    // the cube over the centre spot: four screens, a ring of light underneath
    const c = CUBE.half;
    for (const [mat, a, b, f, uv] of [["video", [-c, -c], [c, -c], NY, [0, 0, 1, 1]], ["video", [c, c], [-c, c], PY, [0, 0, 1, 1]], ["video", [c, -c], [c, c], PX, [0, 0, 1, 1]], ["video", [-c, c], [-c, -c], NX, [0, 0, 1, 1]]])
      rect(mat, [a[0] * 1.0, a[1], Z(CUBE.h0)], [b[0] - a[0], b[1] - a[1], 0], [0, 0, CUBE.h1 - CUBE.h0], uv, f);
    flat("cube_bottom", -c, -c, c, c, CUBE.h0, 2 * c, DOWN); flat("steel_dark", -c, -c, c, c, CUBE.h1, 64, UP);
    for (const [ex, ey] of [[-1, -1], [1, -1], [1, 1], [-1, 1]]) box("steel_dark", ex * (c - 16) - 1, ey * (c - 16) - 1, CUBE.h1, ex * (c - 16) + 1, ey * (c - 16) + 1, roofH(ex * (c - 16)) - 4, {}, ["top", "bottom"]);
    // softer lights for the stands and the ends
    for (const ly of [-1290, -860, -430, 0, 430, 860, 1290]) {
      lights.push({ at: [WEST.x - 150, ly, Z(300)], brightness: 1.1, range: 900 }, { at: [EAST.x + 100, ly, Z(330)], brightness: 1.1, range: 900 });
      lights.push({ at: [HALL.x0 + 90, ly, Z(WEST_TOP.h + 110)], brightness: 0.6, range: 420, color: "255 214 160" });
    }
    for (const s of [1, -1]) for (const lx of [-560, 0, 560]) lights.push({ at: [lx, s * (END.y + 110), Z(330)], brightness: 1.0, range: 900, color: s > 0 ? "255 205 195" : "195 212 255" });
  }
  return { scene, crowd, lights };
}
export const HALL_BOUNDS = { x0: HALL.x0, x1: HALL.x1, y: HALL.y };
export { FLOOR };
void WALK;
