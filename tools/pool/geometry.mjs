// SoccerMod 1v1 cage: every face of the map, grouped by material and by model (opts.group).
// layout.mjs holds the numbers. Collision is a handful of plain thick boxes (physOnly: in the
// collision mesh only), so the ball and the players meet simple, solid shapes.
import { FLOOR, Z, POOL, CAGE, GOAL, DECK, ARCADE, GALLERY, HALL, SKYLIGHT, LAMPS, TOWER, BLOCKS, WORKLIGHTS, lanes, pitchLines } from "./layout.mjs";
import { Scene } from "../arena/lib/mesh.mjs";

const UP = [0, 0, 1], DOWN = [0, 0, -1], PX = [1, 0, 0], NX = [-1, 0, 0], PY = [0, 1, 0], NY = [0, -1, 0];
const M = (name) => `materials/soccermod_pool/${name}.vmat`;

export function buildPool() {
  const scene = new Scene();
  const crowd = [];     // rows of onlookers: { a: [x, y], b: [x, y], h, look: [nx, ny], group, name, sparse }
  const lights = [];    // baked lights: { at: [x, y, z], brightness, range, color? }
  let G = "pool";       // the model the next faces go into
  const O = (o = {}) => ({ group: G, ...o });
  let seed = 20261003;
  const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296), pick = (a) => a[Math.floor(rnd() * a.length)];

  // ---- helpers -------------------------------------------------------------------------------------
  const SIDE = { s0: "y0", s1: "x1", s2: "y1", s3: "x0", top: "top", bottom: "bottom" };
  // box from two corners; heights above the pool's bottom. skip: "x0" "x1" "y0" "y1" "top" "bottom"
  const box = (mat, xa, ya, h0, xb, yb, h1, o = {}, skip = [], uv = 64) => {
    const x0 = Math.min(xa, xb), x1 = Math.max(xa, xb), y0 = Math.min(ya, yb), y1 = Math.max(ya, yb);
    const b = [[x0, y0, Z(h0)], [x1, y0, Z(h0)], [x1, y1, Z(h0)], [x0, y1, Z(h0)]], t = b.map((p) => [p[0], p[1], Z(h1)]);
    scene.prism(M(mat), b, t, uv, O(o), Object.keys(SIDE).filter((k) => skip.includes(SIDE[k])));
  };
  const solid = (xa, ya, h0, xb, yb, h1) => box("collide", xa, ya, h0, xb, yb, h1, { solid: true, physOnly: true });
  const flat = (mat, x0, y0, x1, y1, h, tile, facing = UP, opts = {}) =>
    scene.quad(M(mat), [x0, y0, Z(h)], [x1, y0, Z(h)], [x1, y1, Z(h)], [x0, y1, Z(h)], [[x0 / tile, -y0 / tile], [x1 / tile, -y0 / tile], [x1 / tile, -y1 / tile], [x0 / tile, -y1 / tile]], facing, O(opts));
  // A wall: from (ax, ay) to (bx, by) on the ground, h0..h1, seen from the side `f` points to.
  // uv = tile size in units (the texture's top is up, u runs along the wall) or [u0, v0, u1, v1] with u0 at the viewer's left.
  const wall = (mat, ax, ay, bx, by, h0, h1, f, uv = 128, opts = {}) => {
    const rx = -f[1], ry = f[0], sa = ax * rx + ay * ry, sb = bx * rx + by * ry;
    const [lx, ly, rgx, rgy, sl, sr] = sa <= sb ? [ax, ay, bx, by, sa, sb] : [bx, by, ax, ay, sb, sa];
    const [u0, v0, u1, v1] = Array.isArray(uv) ? uv : [sl / uv, -h1 / uv, sr / uv, -h0 / uv];
    scene.quad(M(mat), [lx, ly, Z(h0)], [rgx, rgy, Z(h0)], [rgx, rgy, Z(h1)], [lx, ly, Z(h1)], [[u0, v1], [u1, v1], [u1, v0], [u0, v0]], [f[0], f[1], 0], O(opts));
  };
  // a picture on a wall: centre (cx, cy) on the wall's plane, `off` in front of it
  const decal = (mat, cx, cy, f, w, h0, h1, off = 0.8, opts = {}, uv = [0, 0, 1, 1]) => {
    const rx = -f[1], ry = f[0], x = cx + f[0] * off, y = cy + f[1] * off;
    wall(mat, x - rx * w / 2, y - ry * w / 2, x + rx * w / 2, y + ry * w / 2, h0, h1, f, uv, opts);
  };
  // a picture lying on the ground, its top towards `up`
  const ground = (mat, cx, cy, w, d, up = [0, 1], h = 0.2, opts = {}) => {
    const rx = up[1], ry = -up[0], p = (a, b) => [cx + rx * a * w / 2 + up[0] * b * d / 2, cy + ry * a * w / 2 + up[1] * b * d / 2, Z(h)];
    scene.quad(M(mat), p(-1, -1), p(1, -1), p(1, 1), p(-1, 1), [[0, 1], [1, 1], [1, 0], [0, 0]], UP, O(opts));
  };
  // a round picture on a wall (the underwater lamps): centre, facing, radius
  const disc = (mat, cx, cy, h, f, r, off, seg = 16) => {
    const rx = -f[1], ry = f[0], x = cx + f[0] * off, y = cy + f[1] * off, ring = [...Array(seg).keys()].map((k) => { const a = k * 2 * Math.PI / seg; return [x + rx * Math.cos(a) * r, y + ry * Math.cos(a) * r, Z(h + Math.sin(a) * r)]; });
    scene.poly(M(mat), ring, [...Array(seg).keys()].map((k) => [0.5 + Math.cos(k * 2 * Math.PI / seg) * 0.5, 0.5 - Math.sin(k * 2 * Math.PI / seg) * 0.5]), [f[0], f[1], 0], O());
  };
  const cylinder = (mat, cx, cy, r, h0, h1, seg = 10, opts = {}, cap = true, r1 = r) => {
    const p = (k, rr, h) => [cx + Math.cos(k * 2 * Math.PI / seg) * rr, cy + Math.sin(k * 2 * Math.PI / seg) * rr, Z(h)];
    for (let k = 0; k < seg; k++) { const n = [Math.cos((k + 0.5) * 2 * Math.PI / seg), Math.sin((k + 0.5) * 2 * Math.PI / seg), 0]; scene.quad(M(mat), p(k, r, h0), p(k + 1, r, h0), p(k + 1, r1, h1), p(k, r1, h1), [[k / seg * 2, (h1 - h0) / 64], [(k + 1) / seg * 2, (h1 - h0) / 64], [(k + 1) / seg * 2, 0], [k / seg * 2, 0]], n, O(opts)); }
    if (cap) { const top = [...Array(seg).keys()].map((k) => p(k, r1, h1)); scene.poly(M(mat), top, top.map((q) => [q[0] / 64, q[1] / 64]), UP, O(opts)); }
  };
  // a thin bar between two points (two crossed strips)
  const bar = (mat, a, b, w = 2, opts = {}) => {
    const d = [b[0] - a[0], b[1] - a[1], b[2] - a[2]], flatD = Math.hypot(d[0], d[1]) > 1e-3, side = flatD ? [-d[1], d[0], 0] : [1, 0, 0], sl = Math.hypot(...side) || 1, s = side.map((v) => v / sl * w / 2), len = Math.hypot(...d) / 32;
    scene.quad(M(mat), [a[0] - s[0], a[1] - s[1], a[2]], [b[0] - s[0], b[1] - s[1], b[2]], [b[0] + s[0], b[1] + s[1], b[2]], [a[0] + s[0], a[1] + s[1], a[2]], [[0, 0], [len, 0], [len, 1], [0, 1]], null, O({ twoSided: true, ...opts }));
    const up2 = flatD ? [0, 0, w / 2] : [0, w / 2, 0];
    scene.quad(M(mat), [a[0] - up2[0], a[1] - up2[1], a[2] - up2[2]], [b[0] - up2[0], b[1] - up2[1], b[2] - up2[2]], [b[0] + up2[0], b[1] + up2[1], b[2] + up2[2]], [a[0] + up2[0], a[1] + up2[1], a[2] + up2[2]], [[0, 0], [len, 0], [len, 1], [0, 1]], null, O({ twoSided: true, ...opts }));
  };
  const { hx, hy, depth: D } = POOL, lid = CAGE.top, TOP = lid + 44, HX = HALL.x, HY = HALL.y, AX = ARCADE.x;
  const mouth = GOAL.half + GOAL.post, gbar = GOAL.height + GOAL.post;

  // =================================================================================================
  // the pool: its bottom (the pitch) with the lane lines and the painted pitch lines, the tiled walls
  // with the underwater lamps, ladders, the goals in the end walls
  // =================================================================================================
  G = "pool";
  {
    flat("pool_floor", -hx, -hy, hx, hy, 0, 128);
    for (const s of [1, -1]) { const y0 = s * hy, y1 = s * (hy + GOAL.depth); flat("pool_floor", -mouth, Math.min(y0, y1), mouth, Math.max(y0, y1), 0, 128); }
    for (const poly of lanes()) scene.poly(M("tile_black"), poly.map(([x, y]) => [x, y, Z(0.12)]), poly.map(([x, y]) => [x / 32, -y / 32]), UP, O());
    // what the water left: dark wet patches, rust under the drain, leaves in a corner
    for (const [x, y, w, d, a] of [[-150, 250, 260, 180, 0.3], [170, -190, 300, 200, 1.2], [60, 330, 180, 130, 2.2], [-210, -330, 200, 150, 0.8], [230, 120, 150, 110, 1.7]]) ground("puddle", x, y, w, d, [Math.cos(a), Math.sin(a)], 0.2);
    ground("drain", 0, 0, 44, 44, [0, 1], 0.26);
    for (const poly of pitchLines()) scene.poly(M("line_white"), poly.map(([x, y]) => [x, y, Z(0.45)]), poly.map(([x, y]) => [x / 48, -y / 48]), UP, O());
    solid(-HX - 40, -HY - 40, -64, HX + 40, HY + 40, 0);

    // the long walls: one picture over the wall's height (dark band under the edge, the old water line, grime at the foot)
    for (const s of [1, -1]) {
      const x = s * hx, look = s > 0 ? NX : PX;
      wall("pool_wall", x, -hy, x, hy, 0, D, look, [0, 0, (2 * hy) / 128, 1]);
      solid(x, -hy - 60, 0, x + s * 44, hy + 60, TOP);
      for (const y of LAMPS.ys) {
        disc("lamp_ring", x, y, LAMPS.h, look, LAMPS.r + 4, 0.6); disc("lamp_glow", x, y, LAMPS.h, look, LAMPS.r, 1);
        decal("rust_run", x, y, look, 22, 4, LAMPS.h - LAMPS.r - 2, 0.5);
        lights.push({ at: [x - s * 26, y, Z(LAMPS.h)], brightness: 0.34, range: 330, color: "150 240 226" });
      }
      // a ladder: two chrome rails over the edge, rungs between them
      for (const ly of [-292, 292]) { for (const e of [-9, 9]) { box("chrome", x - s * 5, ly + e - 1.2, 18, x - s * 2.6, ly + e + 1.2, D + 30, {}, ["bottom"], 16); box("chrome", x - s * 5, ly + e - 1.2, D + 27.6, x + s * 14, ly + e + 1.2, D + 30, {}, [], 16); }
        for (let k = 0; k < 5; k++) box("chrome", x - s * 4.6, ly - 9, 24 + k * 22, x - s * 3, ly + 9, 26 + k * 22, {}, ["y0", "y1"], 16); }
      // depth markings and old signs on the band under the edge
      decal(s > 0 ? "sign_depth_deep" : "sign_nodiving", x, 330, look, 96, D - 30, D - 6, 0.6); decal(s > 0 ? "sign_nodiving" : "sign_depth_shallow", x, -330, look, 96, D - 30, D - 6, 0.6);
    }
    // graffiti on the tiles
    decal("graf_crew", -hx, -120, PX, 250, 14, 96, 0.7); decal("graf_gol", hx, 150, NX, 210, 12, 100, 0.7); decal("graf_tags", hx, -140, NX, 150, 18, 92, 0.7); decal("graf_ball", -hx, 205, PX, 84, 14, 98, 0.7);

    for (const s of [1, -1]) {
      const gy = s * hy, back = gy + s * GOAL.depth, look = s > 0 ? NY : PY, ya = Math.min(gy, back), yb = Math.max(gy, back), P = GOAL.post;
      // the end wall beside and above the goal (the picture's top part over the mouth)
      wall("pool_wall", -hx, gy, -mouth, gy, 0, D, look, [0, 0, (hx - mouth) / 128, 1]); wall("pool_wall", mouth, gy, hx, gy, 0, D, look, [0, 0, (hx - mouth) / 128, 1]);
      wall("pool_wall", -mouth, gy, mouth, gy, gbar, D, look, [0, 0, (2 * mouth) / 128, (D - gbar) / D]);
      for (const x of LAMPS.xs) { disc("lamp_ring", x, gy, LAMPS.h, look, LAMPS.r + 4, 0.6); disc("lamp_glow", x, gy, LAMPS.h, look, LAMPS.r, 1); decal("rust_run", x, gy, look, 22, 4, LAMPS.h - LAMPS.r - 2, 0.5); lights.push({ at: [x, gy - s * 26, Z(LAMPS.h)], brightness: 0.34, range: 330, color: "150 240 226" }); }
      decal(s > 0 ? "graf_wild" : "graf_rei", s > 0 ? -196 : 200, gy, look, 150, 70, 112, 0.7);
      // the goal: a white steel frame on the line, behind it a niche of dark tiles with a net and a lamp in its roof
      for (const e of [-1, 1]) box("goal_frame", e * GOAL.half, gy - P / 2, 0, e * mouth, gy + P / 2, gbar, { solid: true }, ["bottom"], 48);
      box("goal_frame", -GOAL.half, gy - P / 2, GOAL.height, GOAL.half, gy + P / 2, gbar, { solid: true }, ["x0", "x1"], 48);
      wall("niche", -mouth, ya, -mouth, yb, 0, gbar, PX, 64); wall("niche", mouth, ya, mouth, yb, 0, gbar, NX, 64); wall("niche", -mouth, back, mouth, back, 0, gbar, look, 64);
      flat("niche", -mouth, ya, mouth, yb, gbar, 64, DOWN);
      flat("lamp_glow", -30, (ya + yb) / 2 - 8, 30, (ya + yb) / 2 + 8, gbar - 0.6, 60, DOWN);
      lights.push({ at: [0, (gy + back) / 2, Z(gbar - 16)], brightness: 0.3, range: 220, color: "150 240 226" });
      const n = 3, net = { twoSided: true };
      wall("goal_net", -mouth + n, ya, -mouth + n, yb, 0, gbar - n, PX, 40, net); wall("goal_net", mouth - n, ya, mouth - n, yb, 0, gbar - n, NX, 40, net);
      wall("goal_net", -mouth + n, back - s * n, mouth - n, back - s * n, 0, gbar - n, look, 40, net); flat("goal_net", -mouth + n, ya, mouth - n, yb, gbar - n - 3, 40, DOWN, net);
      // collision: the end wall beside and above the mouth, then the niche (sides, back, ceiling)
      solid(-hx - 44, gy, 0, -mouth, gy + s * 44, TOP); solid(mouth, gy, 0, hx + 44, gy + s * 44, TOP); solid(-mouth, gy, gbar, mouth, gy + s * 44, TOP);
      solid(-mouth - 40, gy, 0, -mouth, back, gbar + 40); solid(mouth, gy, 0, mouth + 40, back, gbar + 40);
      solid(-mouth - 40, back, 0, mouth + 40, back + s * 40, gbar + 40); solid(-mouth - 40, gy, gbar, mouth + 40, back + s * 40, gbar + 40);
    }
    // the ball's ceiling: on the cage's lid
    solid(-hx - 44, -hy - 60, lid, hx + 44, hy + 60, TOP);
  }

  // =================================================================================================
  // the cage on the pool's edge: steel posts, mesh up to the lid, a mesh lid on steel beams
  // =================================================================================================
  G = "cage";
  {
    const m = 4, x1 = hx + m, y1 = hy + m, mh = (lid - D) / 48;
    for (const s of [1, -1]) {
      wall("mesh", s * x1, -y1, s * x1, y1, D, lid, s > 0 ? NX : PX, [0, 0, (2 * y1) / 48, mh], { twoSided: true });
      wall("mesh", -x1, s * y1, x1, s * y1, D, lid, s > 0 ? NY : PY, [0, 0, (2 * x1) / 48, mh], { twoSided: true });
      for (let y = -y1; y <= y1 + 1; y += CAGE.bay) box("steel_cage", s * x1 - 3, y - 3, D, s * x1 + 3, y + 3, lid + 4, {}, ["bottom"], 32);
      for (let x = -x1 + 2 * x1 / 6; x < x1 - 1; x += 2 * x1 / 6) box("steel_cage", x - 3, s * y1 - 3, D, x + 3, s * y1 + 3, lid + 4, {}, ["bottom"], 32);
      for (const h of [D + 2, (D + lid) / 2, lid]) { box("steel_cage", s * x1 - 2, -y1, h - 2, s * x1 + 2, y1, h + 2, {}, ["y0", "y1"], 32); box("steel_cage", -x1, s * y1 - 2, h - 2, x1, s * y1 + 2, h + 2, {}, ["x0", "x1"], 32); }
    }
    flat("mesh_top", -x1, -y1, x1, y1, lid, 64, DOWN, { twoSided: true });
    for (let y = -y1 + CAGE.bay; y < y1 - 1; y += 2 * CAGE.bay) box("steel_cage", -x1, y - 3, lid, x1, y + 3, lid + 6, {}, ["x0", "x1"], 32);
    box("steel_cage", -3, -y1, lid, 3, y1, lid + 6, {}, ["y0", "y1"], 32);
  }

  // =================================================================================================
  // the deck round the pool, the arcade and the cabins behind it, the gallery above, the tower, the blocks
  // =================================================================================================
  G = "deck";
  {
    // deck tiles round the pool, a rim of pale stone at the edge
    flat("deck_tile", -HX, -HY, -hx, HY, D, 128); flat("deck_tile", hx, -HY, HX, HY, D, 128); flat("deck_tile", -hx, hy, hx, HY, D, 128); flat("deck_tile", -hx, -HY, hx, -hy, D, 128);
    for (const s of [1, -1]) { flat("coping", s > 0 ? hx : -hx - 18, -hy - 18, s > 0 ? hx + 18 : -hx, hy + 18, D + 0.4, 64); flat("coping", -hx, s > 0 ? hy : -hy - 18, hx, s > 0 ? hy + 18 : -hy, D + 0.4, 64); }
    for (const s of [1, -1]) {
      const x = s * AX, look = s > 0 ? NX : PX, away = s > 0 ? PX : NX, c = ARCADE.col / 2;
      // columns with a base and a head, a beam on them; the arches are cut-out pictures between the columns
      const ys = []; for (let y = -HY + ARCADE.bay / 2 + 4; y < HY; y += ARCADE.bay) ys.push(y);
      for (let k = 0; k <= ys.length; k++) {
        const y = k < ys.length ? ys[k] - ARCADE.bay / 2 : ys[ys.length - 1] + ARCADE.bay / 2;
        box("column", x - c, y - c, D, x + c, y + c, ARCADE.spring, {}, ["bottom", "top"], 64);
        box("column", x - c - 4, y - c - 4, D, x + c + 4, y + c + 4, D + 14, {}, ["bottom"], 64); box("column", x - c - 4, y - c - 4, ARCADE.spring - 12, x + c + 4, y + c + 4, ARCADE.spring, {}, ["top"], 64);
        if (k % 2 === 1) decal("buoy", x - s * c, y, look, 34, D + 96, D + 130, 0.8, { twoSided: true });
      }
      for (const y of ys) { wall("arch", x - s * c, y - ARCADE.bay / 2, x - s * c, y + ARCADE.bay / 2, ARCADE.spring - 70, ARCADE.top, look, [0, 0, 1, 1]); wall("arch", x + s * c, y - ARCADE.bay / 2, x + s * c, y + ARCADE.bay / 2, ARCADE.spring - 70, ARCADE.top, away, [0, 0, 1, 1]); }
      // the gallery: its front, its underside, its floor, a balustrade with a rail
      wall("wall_plaster", x - s * c, -HY, x - s * c, HY, ARCADE.top, GALLERY.h, look, 128);
      flat("ceiling_plaster", s > 0 ? x - c : -HX, -HY, s > 0 ? HX : x + c, HY, ARCADE.spring + 36, 128, DOWN);
      flat("gallery_floor", s > 0 ? x - c : -HX, -HY, s > 0 ? HX : x + c, HY, GALLERY.h, 96);
      wall("balustrade", x - s * (c - 2), -HY, x - s * (c - 2), HY, GALLERY.h, GALLERY.h + GALLERY.rail, look, [0, 0, (2 * HY) / 48, 1], { twoSided: true });
      box("wood_dark", x - s * (c - 4), -HY, GALLERY.h + GALLERY.rail, x - s * (c + 2), HY, GALLERY.h + GALLERY.rail + 4, {}, ["y0", "y1"], 64);
      // warm bulbs along the rail
      for (let y = -HY + 30; y < HY; y += 42) for (const [dx, dy] of [[2.4, 0], [0, 2.4]]) scene.quad(M("bulb"), [x - s * c - dx, y - dy, Z(GALLERY.h + GALLERY.rail + 4)], [x - s * c + dx, y + dy, Z(GALLERY.h + GALLERY.rail + 4)], [x - s * c + dx, y + dy, Z(GALLERY.h + GALLERY.rail + 9.5)], [x - s * c - dx, y - dy, Z(GALLERY.h + GALLERY.rail + 9.5)], [[0, 1], [1, 1], [1, 0], [0, 0]], null, O({ twoSided: true }));
      for (const y of [-520, -170, 170, 520]) lights.push({ at: [x - s * 30, y, Z(GALLERY.h + 60)], brightness: 0.22, range: 420, color: "255 200 130" });
      // behind the arcade: the row of changing cabins, white tiles above the doors; the gallery's wall with arched windows
      const wx = s * HX;
      wall("cabins", wx, -HY, wx, HY, D, D + 112, look, [0, 0, (2 * HY) / 720, 1]); wall("wall_tile", wx, -HY, wx, HY, D + 112, ARCADE.spring + 36, look, 128);
      wall("wall_tile", wx, -HY, wx, HY, GALLERY.h, GALLERY.h + 60, look, 128); wall("wall_plaster", wx, -HY, wx, HY, GALLERY.h + 60, HALL.eave, look, 128);
      for (const y of ys) decal("win_arch", wx, y, look, 84, GALLERY.h + 76, GALLERY.h + 196, 0.8);
      // onlookers: at the cage on the deck, and along the gallery's rail
      crowd.push({ a: [s * (hx + 32), -hy + 30], b: [s * (hx + 32), hy - 30], h: D, look: [-s, 0], group: 2, name: s > 0 ? "east" : "west", sparse: 0.78 });
      crowd.push({ a: [x + s * 2, -HY + 40], b: [x + s * 2, HY - 40], h: GALLERY.h, look: [-s, 0], group: 2, name: s > 0 ? "east" : "west", sparse: 0.55 });
    }
    // the diving tower at the deep end: a concrete mast, two platforms with pipe rails, a springboard beside it
    {
      const T = TOWER, m = T.mast / 2;
      box("concrete", -m, T.y - m, D, m, T.y + m, D + 300, {}, ["bottom"], 96);
      for (const p of T.platforms) {
        const y0 = T.y - p.len, h = D + p.h;
        box("concrete", -p.w / 2, y0, h - 9, p.w / 2, T.y + m, h, {}, [], 96);
        for (const e of [-1, 1]) wall("pipe_rail", e * p.w / 2, y0, e * p.w / 2, T.y + m, h, h + 36, e > 0 ? PX : NX, [0, 0, (p.len + m) / 44, 1], { twoSided: true });
        crowd.push({ a: [-p.w / 2 + 12, y0 + 14], b: [p.w / 2 - 12, y0 + 14], h, look: [0, -1], group: 0, name: "end_red", sparse: 0.9 });
      }
      wall("ladder_pic", -18, T.y + m + 1, 18, T.y + m + 1, D, D + 264, PY, [0, 0, 1, 6], { twoSided: true });
      const b = T.board; box("steel_cage", b.x - 16, T.y - 40, D, b.x + 16, T.y, D + b.h - 4, {}, ["bottom"], 32); box("wood_dark", b.x - 13, T.y - b.len, D + b.h - 4, b.x + 13, T.y, D + b.h, {}, [], 48);
      // the home end's onlookers on the deck behind the cage
      crowd.push({ a: [-hx + 20, hy + 34], b: [hx - 20, hy + 34], h: D, look: [0, -1], group: 0, name: "end_red", sparse: 0.7 });
    }
    // the starting blocks at the away end, onlookers behind them
    for (const [k, x] of BLOCKS.xs.entries()) { box("block", x - 19, BLOCKS.y - 22, D, x + 19, BLOCKS.y + 22, D + 22, {}, ["bottom"], 44); scene.quad(M("block_top"), [x - 19, BLOCKS.y - 22, Z(D + 30)], [x + 19, BLOCKS.y - 22, Z(D + 30)], [x + 19, BLOCKS.y + 22, Z(D + 22)], [x - 19, BLOCKS.y + 22, Z(D + 22)], [[k / 5, 1], [(k + 1) / 5, 1], [(k + 1) / 5, 0], [k / 5, 0]], UP, O({ twoSided: true })); }
    crowd.push({ a: [-hx + 20, -(hy + 84)], b: [hx - 20, -(hy + 84)], h: D, look: [0, 1], group: 1, name: "end_blue", sparse: 0.7 });
  }

  // =================================================================================================
  // the hall: end walls up to the vault, the vault with its glass middle and steel ribs
  // =================================================================================================
  G = "hall";
  {
    const prof = [...HALL.roof.slice().reverse().map(([x, h]) => [-x, h]), ...HALL.roof.slice(1)];           // -x .. +x
    for (const s of [1, -1]) {
      const y = s * HY, look = s > 0 ? NY : PY;
      wall("wall_tile", -HX, y, HX, y, D, 300, look, 128);
      const pts = [[-HX, 300], [HX, 300], ...prof.slice().reverse()].map(([x, h]) => [x, y, Z(h)]);
      scene.poly(M("wall_plaster"), pts, pts.map((p) => [p[0] / 128, -(p[2] - FLOOR) / 128]), [look[0], look[1], 0], O());
      box("wall_trim", -HX, y - s * 5, 296, HX, y, 306, {}, [s > 0 ? "y1" : "y0"], 64);
    }
    // deep end: a round window with the moon behind it, the old sign; away end: the mosaic, the clock, the name
    decal("win_round", 0, HY, NY, 150, 450, 600, 0.8); decal("sign_deep", -330, HY, NY, 220, 330, 400, 0.8); decal("sign_rules", 330, HY, NY, 150, 316, 428, 0.8);
    decal("mosaic", 0, -HY, PY, 520, 316, 546, 0.8); decal("clock", 392, -HY, PY, 96, 420, 516, 0.8); decal("sign_name", 0, -HY, PY, 520, 554, 604, 0.8);
    decal("scoreboard", -398, -HY, PY, 190, 330, 450, 0.8);
    // the vault: glass in the middle, plaster at the sides; steel ribs under it
    for (let k = 0; k + 1 < prof.length; k++) {
      const [xa, ha] = prof[k], [xb, hb] = prof[k + 1], glass = Math.max(Math.abs(xa), Math.abs(xb)) <= SKYLIGHT;
      scene.quad(M(glass ? "skylight" : "ceiling_plaster"), [xa, -HY, Z(ha)], [xb, -HY, Z(hb)], [xb, HY, Z(hb)], [xa, HY, Z(ha)], glass ? [[0, 0], [(xb - xa) / 95, 0], [(xb - xa) / 95, (2 * HY) / 95], [0, (2 * HY) / 95]] : [[xa / 128, -HY / 128], [xb / 128, -HY / 128], [xb / 128, HY / 128], [xa / 128, HY / 128]], DOWN, O({ twoSided: glass }));
      for (let y = -HY + ARCADE.bay / 2 + 4 - ARCADE.bay / 2; y <= HY; y += ARCADE.bay) scene.quad(M("steel_rib"), [xa, y, Z(ha - 16)], [xb, y, Z(hb - 16)], [xb, y, Z(hb)], [xa, y, Z(ha)], [[0, 1], [(xb - xa) / 64, 1], [(xb - xa) / 64, 0], [0, 0]], null, O({ twoSided: true }));
    }
    for (const x of [-SKYLIGHT, 0, SKYLIGHT]) { const h = x === 0 ? 650 : 596; box("steel_cage", x - 3, -HY, h - 12, x + 3, HY, h - 4, {}, ["y0", "y1"], 48); }
  }

  // =================================================================================================
  // what the players brought: tripod work lights, a generator and its cables, speakers, crates, loungers
  // =================================================================================================
  G = "props";
  {
    for (const [x, y] of WORKLIGHTS) {
      const hub = [x, y, Z(D + 96)], tl = Math.hypot(x, y), fx = -x / tl, fy = -y / tl;
      for (let k = 0; k < 3; k++) { const a = k * 2 * Math.PI / 3 + 0.4; bar("steel_cage", [x + Math.cos(a) * 26, y + Math.sin(a) * 26, Z(D)], hub, 2); }
      bar("steel_cage", hub, [x, y, Z(D + 176)], 2.4);
      box("worklight", x - 15, y - 15, D + 168, x + 15, y + 15, D + 192, {}, [], 30);
      scene.quad(M("lamp_warm"), [x + fx * 15.4 - fy * 12, y + fy * 15.4 + fx * 12, Z(D + 171)], [x + fx * 15.4 + fy * 12, y + fy * 15.4 - fx * 12, Z(D + 171)], [x + fx * 15.4 + fy * 12, y + fy * 15.4 - fx * 12, Z(D + 189)], [x + fx * 15.4 - fy * 12, y + fy * 15.4 + fx * 12, Z(D + 189)], [[0, 1], [1, 1], [1, 0], [0, 0]], [fx, fy, 0], O({ twoSided: true }));
      lights.push({ at: [x + fx * 50, y + fy * 50, Z(D + 176)], brightness: 0.75, range: 900, color: "255 206 150" });
    }
    // generator, cables to the lamps, speakers
    box("generator", 470, 600, D, 540, 644, D + 44, {}, ["bottom"], 70); box("steel_cage", 474, 604, D + 44, 536, 640, D + 50, {}, ["bottom"], 32);
    for (const [ax, ay, bx, by] of [[470, 620, 392, 548], [505, 600, 390, -535], [470, 610, -380, 548]]) { const n = 10; for (let k = 0; k < n; k++) { const t0 = k / n, t1 = (k + 1) / n, w = (t) => Math.sin(t * 9 + ax) * 14 * Math.sin(t * Math.PI); const p = (t) => [ax + (bx - ax) * t + w(t) * (by - ay > 0 ? 1 : -1) * 0.3, ay + (by - ay) * t + w(t), Z(D + 1.2)]; bar("cable", p(t0), p(t1), 2.2); } }
    for (const [x, y] of [[-520, -640], [-470, -660]]) { box("speaker", x - 20, y - 16, D, x + 20, y + 16, D + 58, {}, ["bottom"], 58); box("speaker", x - 17, y - 14, D + 58, x + 17, y + 14, D + 100, {}, ["bottom"], 42); }
    for (const [x, y, n] of [[380, -620, 3], [420, -650, 2], [-400, 650, 2]]) for (let k = 0; k < n; k++) box("crate", x - 16, y - 16, D + k * 15, x + 16, y + 16, D + k * 15 + 14, {}, ["bottom"], 32);
    // two old loungers on the deck
    for (const [x, y] of [[-395, -200], [-395, 60]]) { box("wood_dark", x - 14, y - 44, D + 12, x + 14, y + 44, D + 15, {}, [], 28); for (const [dx, dy] of [[-12, -40], [12, -40], [12, 40], [-12, 40]]) box("wood_dark", x + dx - 1.5, y + dy - 1.5, D, x + dx + 1.5, y + dy + 1.5, D + 12, {}, ["bottom", "top"], 16); scene.quad(M("wood_dark"), [x - 14, y + 20, Z(D + 15)], [x + 14, y + 20, Z(D + 15)], [x + 14, y + 44, Z(D + 36)], [x - 14, y + 44, Z(D + 36)], [[0, 0], [1, 0], [1, 1], [0, 1]], UP, O({ twoSided: true })); }
  }
  return { scene, crowd, lights };
}
export { FLOOR };
