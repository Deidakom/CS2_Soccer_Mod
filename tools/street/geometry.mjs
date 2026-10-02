// SoccerMod street arena: every face of the map, grouped by material and by model (opts.group).
// layout.mjs holds the numbers. Collision is a handful of plain thick boxes (physOnly: in the
// collision mesh only), so the ball and the players meet simple, solid shapes.
import { FLOOR, Z, COURT, WEST_WALL, EAST_WALL, END_WALL, FENCE, GOAL, WEST, EAST, EL, NORTH, SOUTH, LAMPS, STRINGS, FAR, RAYS, rayOf, courtLines } from "./layout.mjs";
import { Scene } from "../arena/lib/mesh.mjs";

const UP = [0, 0, 1], DOWN = [0, 0, -1], PX = [1, 0, 0], NX = [-1, 0, 0], PY = [0, 1, 0], NY = [0, -1, 0];
const M = (name) => `materials/soccermod_street/${name}.vmat`;

export function buildStreet() {
  const scene = new Scene();
  const crowd = [];     // rows of onlookers: { a: [x, y], b: [x, y], h, look: [nx, ny], group: 2, name, sparse? }
  const lights = [];    // baked lights: { at: [x, y, z], brightness, range, color? }
  let G = "court";      // the model the next faces go into
  const O = (o = {}) => ({ group: G, ...o });
  let seed = 20261002;
  const rnd = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296), pick = (a) => a[Math.floor(rnd() * a.length)];

  // ---- helpers -------------------------------------------------------------------------------------
  const SIDE = { s0: "y0", s1: "x1", s2: "y1", s3: "x0", top: "top", bottom: "bottom" };
  // box from two corners; heights above the floor. skip: "x0" "x1" "y0" "y1" "top" "bottom"
  const box = (mat, xa, ya, h0, xb, yb, h1, o = {}, skip = [], uv = 64) => {
    const x0 = Math.min(xa, xb), x1 = Math.max(xa, xb), y0 = Math.min(ya, yb), y1 = Math.max(ya, yb);
    const b = [[x0, y0, Z(h0)], [x1, y0, Z(h0)], [x1, y1, Z(h0)], [x0, y1, Z(h0)]], t = b.map((p) => [p[0], p[1], Z(h1)]);
    scene.prism(M(mat), b, t, uv, O(o), Object.keys(SIDE).filter((k) => skip.includes(SIDE[k])));
  };
  const solid = (xa, ya, h0, xb, yb, h1) => box("collide", xa, ya, h0, xb, yb, h1, { solid: true, physOnly: true });
  const flat = (mat, x0, y0, x1, y1, h, tile, facing = UP, opts = {}) =>
    scene.quad(M(mat), [x0, y0, Z(h)], [x1, y0, Z(h)], [x1, y1, Z(h)], [x0, y1, Z(h)], [[x0 / tile, -y0 / tile], [x1 / tile, -y0 / tile], [x1 / tile, -y1 / tile], [x0 / tile, -y1 / tile]], facing, O(opts));
  // A wall: from (ax, ay) to (bx, by) on the ground, h0..h1, seen from the side `f` points to.
  // uv = tile size in units (the texture's top is up, u runs along the wall) or [u0, v0, u1, v1]
  // with u0 at the viewer's left.
  const wall = (mat, ax, ay, bx, by, h0, h1, f, uv = 128, opts = {}) => {
    const rx = -f[1], ry = f[0], sa = ax * rx + ay * ry, sb = bx * rx + by * ry;        // along the viewer's right
    const [lx, ly, rgx, rgy, sl, sr] = sa <= sb ? [ax, ay, bx, by, sa, sb] : [bx, by, ax, ay, sb, sa];
    const [u0, v0, u1, v1] = Array.isArray(uv) ? uv : [sl / uv, -h1 / uv, sr / uv, -h0 / uv];
    scene.quad(M(mat), [lx, ly, Z(h0)], [rgx, rgy, Z(h0)], [rgx, rgy, Z(h1)], [lx, ly, Z(h1)], [[u0, v1], [u1, v1], [u1, v0], [u0, v0]], [f[0], f[1], 0], O(opts));
  };
  // a picture on a wall: centre (cx, cy) on the wall's plane, `off` in front of it
  const decal = (mat, cx, cy, f, w, h0, h1, off = 0.5, opts = {}, uv = [0, 0, 1, 1]) => {
    const rx = -f[1], ry = f[0], x = cx + f[0] * off, y = cy + f[1] * off;
    wall(mat, x - rx * w / 2, y - ry * w / 2, x + rx * w / 2, y + ry * w / 2, h0, h1, f, uv, opts);
  };
  // a picture lying on the ground, its top towards the direction `up` ([ux, uy])
  const ground = (mat, cx, cy, w, d, up = [0, 1], h = 0.3, opts = {}) => {
    const rx = up[1], ry = -up[0], p = (a, b) => [cx + rx * a * w / 2 + up[0] * b * d / 2, cy + ry * a * w / 2 + up[1] * b * d / 2, Z(h)];
    scene.quad(M(mat), p(-1, -1), p(1, -1), p(1, 1), p(-1, 1), [[0, 1], [1, 1], [1, 0], [0, 0]], UP, O(opts));
  };
  const cylinder = (mat, cx, cy, r, h0, h1, seg = 12, opts = {}, cap = true, r1 = r) => {
    const p = (k, rr, h) => [cx + Math.cos(k * 2 * Math.PI / seg) * rr, cy + Math.sin(k * 2 * Math.PI / seg) * rr, Z(h)];
    for (let k = 0; k < seg; k++) {
      const n = [Math.cos((k + 0.5) * 2 * Math.PI / seg), Math.sin((k + 0.5) * 2 * Math.PI / seg), 0];
      scene.quad(M(mat), p(k, r, h0), p(k + 1, r, h0), p(k + 1, r1, h1), p(k, r1, h1), [[k / seg * 2, (h1 - h0) / 64], [(k + 1) / seg * 2, (h1 - h0) / 64], [(k + 1) / seg * 2, 0], [k / seg * 2, 0]], n, O(opts));
    }
    if (cap) { const top = [...Array(seg).keys()].map((k) => p(k, r1, h1)); scene.poly(M(mat), top, top.map((q) => [q[0] / 64, q[1] / 64]), UP, O(opts)); }
  };
  // a thin cable / bar between two points (two crossed strips)
  const cable = (mat, a, b, w = 1.2, opts = {}) => {
    const d = [b[0] - a[0], b[1] - a[1], b[2] - a[2]], horizontal = Math.hypot(d[0], d[1]) > 1e-3, side = horizontal ? [-d[1], d[0], 0] : [1, 0, 0], sl = Math.hypot(...side) || 1;
    const s = side.map((v) => v / sl * w / 2), len = Math.hypot(...d) / 32;
    scene.quad(M(mat), [a[0] - s[0], a[1] - s[1], a[2]], [b[0] - s[0], b[1] - s[1], b[2]], [b[0] + s[0], b[1] + s[1], b[2]], [a[0] + s[0], a[1] + s[1], a[2]], [[0, 0], [len, 0], [len, 1], [0, 1]], null, O({ twoSided: true, ...opts }));
    if (horizontal) scene.quad(M(mat), [a[0], a[1], a[2] - w / 2], [b[0], b[1], b[2] - w / 2], [b[0], b[1], b[2] + w / 2], [a[0], a[1], a[2] + w / 2], [[0, 0], [len, 0], [len, 1], [0, 1]], null, O({ twoSided: true, ...opts }));
  };
  // a building: a box of `mat` (walls tiled by 128), flat roof; faces: which sides are drawn
  const FACE = { w: (b) => [b.x0, b.y1, b.x0, b.y0, NX], e: (b) => [b.x1, b.y0, b.x1, b.y1, PX], s: (b) => [b.x0, b.y0, b.x1, b.y0, NY], n: (b) => [b.x1, b.y1, b.x0, b.y1, PY] };
  const building = (b, mat, faces = "wesn", tile = 128) => {
    for (const k of faces) { const [ax, ay, bx, by, f] = FACE[k](b); wall(mat, ax, ay, bx, by, b.h0 ?? 0, b.h1, f, tile); }
    flat(b.roof ?? "roof_tar", b.x0, b.y0, b.x1, b.y1, b.h1, 128);
  };
  // windows on one face of a building: a grid of pictures. spec: cols, rows, w, h, first (sill of the
  // lowest row above b.h0), pitch (floor height), mats (picked per window), margin
  const windows = (b, k, spec) => {
    const [ax, ay, bx, by, f] = FACE[k](b), len = Math.hypot(bx - ax, by - ay), m = spec.margin ?? 40, step = (len - 2 * m) / spec.cols;
    for (let r = 0; r < spec.rows; r++) for (let c = 0; c < spec.cols; c++) {
      if (spec.skip?.(c, r)) continue;
      const t = (m + (c + 0.5) * step) / len, cx = ax + (bx - ax) * t, cy = ay + (by - ay) * t, h0 = (b.h0 ?? 0) + spec.first + r * spec.pitch;
      decal(pick(spec.mats), cx, cy, f, spec.w, h0, h0 + spec.h, spec.off ?? 2.5);
      if (spec.sill) { const rx = -f[1], ry = f[0]; box(spec.sill, cx - rx * (spec.w / 2 + 3) + f[0] * 0.5, cy - ry * (spec.w / 2 + 3) + f[1] * 0.5, h0 - 4, cx + rx * (spec.w / 2 + 3) + f[0] * 5, cy + ry * (spec.w / 2 + 3) + f[1] * 5, h0, {}, [], 32); }
    }
  };
  // a palm: a slim trunk and a crown of fronds that arch out and hang down (cut-out pictures)
  const palm = (x, y, h, turn = 0) => {
    cylinder("palm_trunk", x, y, 8.5, 0, h, 8, {}, false, 5.5);
    for (let k = 0; k < 10; k++) {
      const a = turn + k * 2 * Math.PI / 10 + (k % 2) * 0.2, dx = Math.cos(a), dy = Math.sin(a), sx = -dy * 22, sy = dx * 22, lift = k % 2 ? 34 : 16, reach = k % 2 ? 62 : 74;
      const p0 = [x, y, Z(h - 4)], p1 = [x + dx * reach, y + dy * reach, Z(h + lift)], p2 = [x + dx * reach * 2.05, y + dy * reach * 2.05, Z(h + lift - 62)];
      scene.quad(M("palm_frond"), [p0[0] - sx * 0.5, p0[1] - sy * 0.5, p0[2]], [p0[0] + sx * 0.5, p0[1] + sy * 0.5, p0[2]], [p1[0] + sx, p1[1] + sy, p1[2]], [p1[0] - sx, p1[1] - sy, p1[2]], [[0, 1], [1, 1], [1, 0.5], [0, 0.5]], null, O({ twoSided: true }));
      scene.quad(M("palm_frond"), [p1[0] - sx, p1[1] - sy, p1[2]], [p1[0] + sx, p1[1] + sy, p1[2]], [p2[0] + sx * 0.7, p2[1] + sy * 0.7, p2[2]], [p2[0] - sx * 0.7, p2[1] - sy * 0.7, p2[2]], [[0, 0.5], [1, 0.5], [1, 0], [0, 0]], null, O({ twoSided: true }));
    }
    cylinder("palm_trunk", x, y, 11, h - 14, h + 2, 8, {}, true, 7);
  };
  const { hx, hy, lid } = COURT, TOP = COURT.ceil;
  const mouth = GOAL.half + GOAL.post / 2, bar = GOAL.height + GOAL.post / 2;

  // =================================================================================================
  // the court: asphalt, worn white lines, graffiti on the ground
  // =================================================================================================
  G = "court";
  {
    for (const s of [1, -1]) { const c = GOAL.housingHalf - GOAL.wall, y0 = s * hy, y1 = s * (hy + GOAL.housingDepth - GOAL.wall); flat("container_floor", -c, Math.min(y0, y1), c, Math.max(y0, y1), 0, 64); }
    // Worn asphalt; on it one big faded painting, sprayed: loose rays from the centre spot, rings, dots (the
    // asphalt shows through everywhere - owner: "more subtle, more abstract and graffiti-like"), then a few
    // abstract pieces, two numbers, a chalk hopscotch, a manhole, cracks, and the white lines on top.
    flat("asphalt", -hx, -hy, hx, hy, 0, 256);
    scene.quad(M("g_court"), [-hx, -hy, Z(0.2)], [hx, -hy, Z(0.2)], [hx, hy, Z(0.2)], [-hx, hy, Z(0.2)], [[0, 1], [1, 1], [1, 0], [0, 0]], UP, O());
    ground("g_centre", 0, 0, 272, 272, [0, 1], 0.45);
    ground("a_arrows", -340, 450, 400, 400, [0.26, 0.97], 0.45); ground("a_bubbles", 350, -480, 360, 360, [-0.5, 0.87], 0.45);
    ground("a_swoosh", -230, -250, 600, 300, [0.34, 0.94], 0.45); ground("a_splat", 330, 310, 300, 300, [0.7, 0.71], 0.45);
    ground("a_scribble", 90, 660, 380, 190, [0.1, -0.99], 0.45); ground("a_wild", -360, -640, 460, 230, [-0.17, 0.98], 0.45);
    ground("g_ten", 470, 60, 130, 130, [-1, 0], 0.45); ground("g_seven", -480, -60, 130, 130, [1, 0], 0.45);
    ground("g_hopscotch", -560, 610, 90, 250, [0, 1], 0.45); ground("g_tags_a", 520, -720, 180, 180, [-0.6, 0.8], 0.45); ground("g_tags_b", -520, 130, 170, 170, [0.9, 0.44], 0.45);
    ground("g_manhole", 480, 250, 46, 46, [0, 1], 0.4); ground("g_crack", -120, 520, 300, 150, [0.3, 0.95], 0.4); ground("g_crack", 200, -330, 300, 150, [-0.8, 0.6], 0.4);
    for (const poly of courtLines()) scene.poly(M("line_white"), poly.map(([x, y]) => [x, y, Z(0.7)]), poly.map(([x, y]) => [x / 48, -y / 48]), UP, O());
    solid(-1500, -1900, -64, 1500, 1700, 0);
  }

  // =================================================================================================
  // the cage: walls and fences all round, the net on top, the two container goals
  // =================================================================================================
  G = "cage";
  {
    const fenceMat = { twoSided: true };
    // west: low painted wall, chain-link fence on it
    // the low wall in six stretches and the retaining wall in four: each has its own picture (wall + graffiti)
    for (let k = 0; k < 6; k++) wall(`wall_west_${k + 1}`, -hx, -hy + k * 300, -hx, -hy + (k + 1) * 300, 0, WEST_WALL.h, PX, [0, 0, 1, 1]);
    wall("concrete", -hx - WEST_WALL.t, -hy, -hx - WEST_WALL.t, hy, 0, WEST_WALL.h, NX, 128);
    flat("concrete_cap", -hx - WEST_WALL.t - 1, -hy, -hx + 1, hy, WEST_WALL.h, 64);
    wall("chainlink", -hx - 4, hy, -hx - 4, -hy, WEST_WALL.h, FENCE.top, PX, [0, 0, (2 * hy) / 64, (FENCE.top - WEST_WALL.h) / 64], fenceMat);
    // east: the retaining wall with the big pieces, fence on top
    for (let k = 0; k < 5; k++) wall(`wall_east_${k + 1}`, hx, hy - k * 360, hx, hy - (k + 1) * 360, 0, EAST_WALL.h, NX, [0, 0, 1, 1]);
    flat("concrete_cap", hx - 1, -hy, hx + EAST_WALL.t + 1, hy, EAST_WALL.h, 64);
    wall("chainlink", hx + 6, -hy, hx + 6, hy, EAST_WALL.h, FENCE.top, NX, [0, 0, (2 * hy) / 64, (FENCE.top - EAST_WALL.h) / 64], fenceMat);
    for (let y = -hy; y <= hy + 1; y += FENCE.bay) {
      box("steel_fence", -hx - 7, y - 2.5, WEST_WALL.h, -hx - 1, y + 2.5, FENCE.top + 4, {}, ["bottom"], 32);
      box("steel_fence", hx + 3, y - 2.5, EAST_WALL.h, hx + 9, y + 2.5, FENCE.top + 4, {}, ["bottom"], 32);
    }
    for (const [x0, x1, hs] of [[-hx - 6, -hx - 2, [WEST_WALL.h + 150, FENCE.top]], [hx + 4, hx + 8, [EAST_WALL.h + 105, FENCE.top]]]) for (const h of hs) box("steel_fence", x0, -hy, h - 2, x1, hy, h + 2, {}, ["y0", "y1"], 32);
    solid(-hx, -hy - 60, 0, -hx - 44, hy + 60, TOP); solid(hx, -hy - 60, 0, hx + 44, hy + 60, TOP);
    // nothing over the court: the walls above go on as invisible ones up to an invisible lid; the viaduct's
    // edge reaches over the court, so the invisible wall steps in there
    solid(-hx - 44, -hy - 60, TOP, hx + 44, hy + 60, TOP + 44);
    solid(EL.cx - EL.width / 2 - 4, -hy - 60, EL.girderH0 - 4, hx + 44, hy + 60, TOP);

    for (const s of [1, -1]) {
      const team = s > 0 ? "red" : "blue", gy = s * hy, look = s > 0 ? NY : PY, away = s > 0 ? PY : NY, H = GOAL.housingHalf, c = H - GOAL.wall;
      const back = gy + s * GOAL.housingDepth, cb = gy + s * (GOAL.housingDepth - GOAL.wall), ch = GOAL.housingH - GOAL.wall, wy = gy + s * END_WALL.t;
      const Y = (a, b) => [Math.min(a, b), Math.max(a, b)];
      // end wall left and right of the container (mural), fence above everything
      wall(`wall_${team}_a`, -hx, gy, -H, gy, 0, END_WALL.h, look, [0, 0, 1, 1]); wall(`wall_${team}_b`, H, gy, hx, gy, 0, END_WALL.h, look, [0, 0, 1, 1]);
      wall("concrete", -hx - 16, wy, -H, wy, 0, END_WALL.h, away, 128); wall("concrete", H, wy, hx + 16, wy, 0, END_WALL.h, away, 128);
      flat("concrete_cap", -hx, Math.min(gy, wy) - 1, -H, Math.max(gy, wy) + 1, END_WALL.h, 64); flat("concrete_cap", H, Math.min(gy, wy) - 1, hx, Math.max(gy, wy) + 1, END_WALL.h, 64);
      const fy = gy + s * 6;
      wall("chainlink", -hx, fy, -H, fy, END_WALL.h, FENCE.top, look, [0, 0, (hx - H) / 64, (FENCE.top - END_WALL.h) / 64], fenceMat); wall("chainlink", H, fy, hx, fy, END_WALL.h, FENCE.top, look, [0, 0, (hx - H) / 64, (FENCE.top - END_WALL.h) / 64], fenceMat);
      wall("chainlink", -H, fy, H, fy, GOAL.housingH, FENCE.top, look, [0, 0, (2 * H) / 64, (FENCE.top - GOAL.housingH) / 64], fenceMat);
      for (const x of [-hx + 3, -428, -H - 3, H + 3, 428, hx - 3]) box("steel_fence", x - 2.5, fy - 3, Math.abs(x) < H + 10 ? 0 : END_WALL.h, x + 2.5, fy + 3, FENCE.top + 4, {}, ["bottom"], 32);
      box("steel_fence", -hx, fy - 2, FENCE.top - 2, hx, fy + 2, FENCE.top + 2, {}, ["x0", "x1"], 32);
      // the container: front frame round the mouth, corrugated inside and outside, doors folded open
      const [oA, oB] = Y(gy, back), [yA, yB] = Y(gy, cb);
      wall(`container_${team}`, -H, gy, -mouth, gy, 0, GOAL.housingH, look, 48); wall(`container_${team}`, mouth, gy, H, gy, 0, GOAL.housingH, look, 48);
      wall(`container_${team}`, -mouth, gy, mouth, gy, bar, GOAL.housingH, look, 48);
      decal(`g_goal_${team}`, 0, gy, look, 2 * mouth, bar + 2, GOAL.housingH - 2, 1.2, { twoSided: false });
      wall("container_in", -c, yA, -c, yB, 0, ch, PX, 48); wall("container_in", c, yA, c, yB, 0, ch, NX, 48); wall("container_in", -c, cb, c, cb, 0, ch, look, 48);
      flat("container_in", -c, yA, c, yB, ch, 48, DOWN);
      wall(`container_${team}`, -H, oA, -H, oB, 0, GOAL.housingH, NX, 48); wall(`container_${team}`, H, oA, H, oB, 0, GOAL.housingH, PX, 48); wall(`container_${team}`, -H, back, H, back, 0, GOAL.housingH, away, 48);
      flat(`container_${team}`, -H, oA, H, oB, GOAL.housingH, 48);
      // collision: the wall beside the container, the container's front beside and above the mouth, its inside
      const [fA, fB] = Y(gy, gy + s * 16), [bA, bB] = Y(cb, cb + s * 40);
      solid(-hx - 44, fA, 0, -H, gy + s * 44, TOP); solid(H, fA, 0, hx + 44, gy + s * 44, TOP);
      solid(-H, fA, 0, -mouth, fB, TOP); solid(mouth, fA, 0, H, fB, TOP); solid(-mouth, fA, bar, mouth, fB, TOP);
      solid(-c - 40, yA, 0, -c, yB, ch + 40); solid(c, yA, 0, c + 40, yB, ch + 40);
      solid(-c - 40, bA, 0, c + 40, bB, ch + 40); solid(-c - 40, Math.min(yA, bA), ch, c + 40, Math.max(yB, bB), ch + 40);
      // onlookers on the container's roof
      crowd.push({ a: [-H + 30, gy + s * 120], b: [H - 30, gy + s * 120], h: GOAL.housingH, look: [0, -s], group: 2, name: s > 0 ? "end_red" : "end_blue", sparse: 0.55 });
    }
  }

  // =================================================================================================
  // west: wave-mosaic sidewalk, the street, a row of low shops; lamp posts and string lights
  // =================================================================================================
  G = "west";
  {
    const Y1 = WEST.y, curb = 5;
    flat("mosaic", WEST.walk1, -Y1, WEST.walk0, Y1, 0, 128);
    box("curb", WEST.walk1 - 6, -Y1, -curb, WEST.walk1, Y1, 0.2, {}, ["bottom", "y0", "y1", "x1"], 64);
    flat("road", WEST.street1, -Y1, WEST.walk1 - 6, Y1, -curb, 256);
    box("curb", WEST.street1, -Y1, -curb, WEST.street1 + 6, Y1, 0.2, {}, ["bottom", "y0", "y1", "x0"], 64);
    flat("sidewalk", WEST.far1, -Y1, WEST.street1, Y1, 0, 128);
    // road paint: centre dashes, a zebra crossing, a manhole
    for (let y = -Y1 + 60; y < Y1; y += 150) ground("road_dash", (WEST.walk1 + WEST.street1) / 2, y, 8, 70, [0, 1], -curb + 0.3);
    for (let k = 0; k < 7; k++) ground("road_zebra", WEST.walk1 - 24 - k * 34, 60, 20, 150, [0, 1], -curb + 0.3);
    ground("g_manhole", -960, -420, 48, 48, [0, 1], -curb + 0.3);
    // the shops, south to north: mercado, garage, barber, deli, pink house
    const X0 = WEST.back, X1 = WEST.far1, shops = [
      { y0: -Y1, y1: -640, h1: 230, mat: "plaster_yellow", front: "shop_mercado" }, { y0: -640, y1: -230, h1: 262, mat: "plaster_orange", front: "shop_garage" },
      { y0: -230, y1: 270, h1: 300, mat: "plaster_blue", front: "shop_barber" }, { y0: 270, y1: 690, h1: 244, mat: "plaster_white", front: "shop_deli" },
      { y0: 690, y1: Y1, h1: 286, mat: "plaster_pink", front: "shop_house" },
    ];
    for (const sdef of shops) {
      const b = { x0: X0, x1: X1, y0: sdef.y0, y1: sdef.y1, h1: sdef.h1 };
      building(b, sdef.mat, "esn");
      // the ground floor as one picture (shop front, door, shutter, sign), upper windows
      decal(sdef.front, X1, (sdef.y0 + sdef.y1) / 2, PX, sdef.y1 - sdef.y0, 0, 132, 1.5);
      windows(b, "e", { cols: Math.round((sdef.y1 - sdef.y0) / 130), rows: sdef.h1 > 250 ? 1 : 0, w: 44, h: 62, first: 162, pitch: 110, mats: ["win_lit", "win_dark", "win_dark", "win_blind"], sill: "concrete_cap" });
      box("cornice", X1 - 2, sdef.y0, sdef.h1 - 12, X1 + 6, sdef.y1, sdef.h1 + 4, {}, ["x0"], 64);
    }
    // awnings over the mercado and the deli
    for (const [y0, y1, mat] of [[-1180, -700, "awning_green"], [310, 650, "awning_red"]]) {
      scene.quad(M(mat), [X1 + 0.5, y0, Z(128)], [X1 + 0.5, y1, Z(128)], [X1 + 62, y1, Z(104)], [X1 + 62, y0, Z(104)], [[0, 0], [(y1 - y0) / 64, 0], [(y1 - y0) / 64, 1], [0, 1]], UP, O({ twoSided: true }));
      wall(mat, X1 + 62, y0, X1 + 62, y1, 92, 104, PX, [0, 0.8, (y1 - y0) / 64, 1], { twoSided: true });
    }
    // roof clutter: blue water tanks, dishes, an old billboard wall painting
    for (const [x, y, hh] of [[-1260, -900, 230], [-1300, 420, 244], [-1250, 980, 286], [-1320, -420, 262]]) { cylinder("tank_blue", x, y, 26, hh, hh + 44, 14, {}, false, 24); cylinder("tank_blue", x, y, 24, hh + 44, hh + 52, 14, {}, true, 9); }
    for (const [x, y, hh] of [[-1215, -760, 230], [-1230, 150, 300], [-1210, 820, 286]]) { box("steel_dark", x - 1.5, y - 1.5, hh, x + 1.5, y + 1.5, hh + 30, {}, ["bottom"], 32); scene.quad(M("dish"), [x + 6, y - 16, Z(hh + 22)], [x + 6, y + 16, Z(hh + 22)], [x + 16, y + 16, Z(hh + 54)], [x + 16, y - 16, Z(hh + 54)], [[0, 1], [1, 1], [1, 0], [0, 0]], PX, O({ twoSided: true })); }
    // lamp posts with warm heads, string lights across the court to the elevated line
    for (const y of LAMPS.ys) {
      cylinder("steel_dark", LAMPS.x, y, 4, 0, LAMPS.h, 8, {}, false, 2.6);
      box("steel_dark", LAMPS.x, y - 1.5, LAMPS.h - 6, LAMPS.x + 46, y + 1.5, LAMPS.h - 3, {}, [], 32);
      box("lamp_head", LAMPS.x + 30, y - 8, LAMPS.h - 14, LAMPS.x + 54, y + 8, LAMPS.h - 6, {}, [], 24);
      flat("lamp_glow", LAMPS.x + 32, y - 6, LAMPS.x + 52, y + 6, LAMPS.h - 14.3, 20, DOWN);
      lights.push({ at: [LAMPS.x + 60, y, Z(LAMPS.h - 40)], brightness: 0.55, range: 620, color: "255 196 128" });
    }
    // props on the sidewalk: hydrant, crates in front of the mercado, a bench, bins
    cylinder("hydrant", -760, 330, 6.5, 0, 22, 10, {}, true, 5.5); cylinder("hydrant", -760, 330, 3.2, 22, 28, 8); box("hydrant", -769, 327, 12, -751, 333, 17, {}, [], 16);
    for (const [x, y, n] of [[-1150, -1010, 3], [-1150, -960, 2], [-1146, -905, 3], [-1150, -760, 1]]) for (let k = 0; k < n; k++) box(pick(["crate_fruit_a", "crate_fruit_b"]), x - 22, y - 16, k * 14, x + 22, y + 16, k * 14 + 13, {}, ["bottom"], 44);
    box("wood_dark", -820, -520, 14, -796, -420, 17, {}, [], 48); for (const y of [-512, -428]) box("steel_dark", -818, y - 2, 0, -798, y + 2, 14, {}, ["bottom"], 32);
    box("wood_dark", -800, -520, 17, -796, -420, 36, {}, [], 48);
    for (const [x, y] of [[-1130, -180], [-1128, 640]]) { box("dumpster", x - 30, y - 46, 4, x + 30, y + 46, 50, {}, ["bottom"], 96); box("dumpster_lid", x - 32, y - 48, 50, x + 32, y + 48, 54, {}, ["bottom"], 96); }
    // onlookers along the fence, on the far sidewalk and at the shops
    crowd.push({ a: [-hx - 40, -hy + 60], b: [-hx - 40, hy - 60], h: 0, look: [1, 0], group: 2, name: "west", sparse: 0.42 });
    crowd.push({ a: [-hx - 78, -520], b: [-hx - 78, 480], h: 0, look: [1, 0], group: 2, name: "west", sparse: 0.25 });
    crowd.push({ a: [WEST.far1 + 30, -560], b: [WEST.far1 + 30, 620], h: 0, look: [1, 0], group: 2, name: "west", sparse: 0.14 });
  }

  // =================================================================================================
  // east: the raised street, the elevated subway line on steel columns, tall tenements behind it
  // =================================================================================================
  G = "east";
  {
    const Y1 = EAST.y, h = EAST.h, x0 = EAST.x0;
    flat("sidewalk", x0, -Y1, 700, Y1, h, 128); flat("road_old", 700, -Y1, 1120, Y1, h, 256); flat("sidewalk", 1120, -Y1, EAST.facade, Y1, h, 128);
    wall("concrete", x0, -hy - 16, x0, -Y1, 0, h, NX, 128); wall("concrete", x0, Y1, x0, hy + 16, 0, h, NX, 128);          // the retaining wall beyond the court
    wall("concrete", x0, -hy - 16, hx, -hy - 16, 0, h, NY, 128); wall("concrete", hx, hy + 16, x0, hy + 16, 0, h, PY, 128);
    // the elevated line: bents of two riveted columns and a cross girder, long plate girders, the deck
    for (let k = 0; k < EL.bents; k++) {
      const y = EL.bentY0 + k * EL.bentEvery;
      for (const cx of EL.colX) {
        box("steel_el", cx - 9, y - 9, h, cx + 9, y + 9, EL.girderH0, {}, ["bottom", "top"], 48);
        box("steel_el", cx - 15, y - 15, h, cx + 15, y + 15, h + 10, {}, ["bottom"], 48);                                 // base plate
        // knee braces to the cross girder
        const s = cx < EL.cx ? 1 : -1;
        scene.quad(M("steel_el"), [cx + s * 9, y - 5, Z(EL.girderH0 - 70)], [cx + s * 9, y + 5, Z(EL.girderH0 - 70)], [cx + s * 80, y + 5, Z(EL.girderH0)], [cx + s * 80, y - 5, Z(EL.girderH0)], [[0, 0], [0.2, 0], [0.2, 2], [0, 2]], null, O({ twoSided: true }));
        if (k % 2 === 0) decal(pick(["g_pillar_a", "g_pillar_b", "g_pillar_c"]), cx - 9, y, NX, 18, h + 14, h + 110, 1);
      }
      box("steel_el", EL.colX[0] - 14, y - 8, EL.girderH0 - 34, EL.colX[1] + 14, y + 8, EL.girderH0, {}, [], 48);
    }
    const yA = -EL.y, yB = EL.y;
    for (const gx of [EL.cx - EL.width / 2, EL.colX[0], EL.colX[1], EL.cx + EL.width / 2]) {
      const edge = gx === EL.cx - EL.width / 2 || gx === EL.cx + EL.width / 2;
      wall(edge ? "girder_el" : "steel_el", gx - 5, yA, gx - 5, yB, EL.girderH0, EL.girderH1, NX, [0, 0, (yB - yA) / 150, 1]); wall(edge ? "girder_el" : "steel_el", gx + 5, yA, gx + 5, yB, EL.girderH0, EL.girderH1, PX, [0, 0, (yB - yA) / 150, 1]);
      flat("steel_el", gx - 5, yA, gx + 5, yB, EL.girderH0, 48, DOWN);
    }
    flat("deck_el", EL.cx - EL.width / 2, yA, EL.cx + EL.width / 2, yB, EL.girderH1 - 8, 96, DOWN);
    flat("track_bed", EL.cx - EL.width / 2, yA, EL.cx + EL.width / 2, yB, EL.deckH, 96);
    for (const tx of [EL.cx - 100, EL.cx + 100]) for (const r of [-29, 29]) box("rail", tx + r - 1.5, yA, EL.deckH, tx + r + 1.5, yB, EL.deckH + 5, {}, ["bottom", "y0", "y1"], 64);
    // walkway railings and a row of signal / lamp masts on the court side
    for (const [rx, f] of [[EL.cx - EL.width / 2 + 2, NX], [EL.cx + EL.width / 2 - 2, PX]]) wall("railing_el", rx, yA, rx, yB, EL.deckH, EL.railH, f, [0, 0, (yB - yA) / 64, 1], { twoSided: true });
    for (let k = 0; k < EL.bents; k += 2) { const y = EL.bentY0 + k * EL.bentEvery + 150; cylinder("steel_el", EL.cx - EL.width / 2 + 6, y, 3, EL.deckH, EL.deckH + 120, 6, {}, false); box("lamp_head", EL.cx - EL.width / 2 - 14, y - 6, EL.deckH + 112, EL.cx - EL.width / 2 + 8, y + 6, EL.deckH + 120, {}, [], 24); }
    // the stairway up to the line at the north end: a covered stair with the sign and the green globes
    {
      const sx0 = 1010, sx1 = 1056, sy0 = 700, n = 24, run = 15, rise = (EL.deckH - h) / n;
      for (let k = 0; k < n; k++) box("steel_el", sx0, sy0 + k * run, h + k * rise, sx1, sy0 + (k + 1) * run, h + (k + 1) * rise, {}, ["bottom", "y1"], 32);
      for (const sx of [sx0, sx1]) scene.quad(M("railing_el"), [sx, sy0, Z(h)], [sx, sy0 + n * run, Z(h + n * rise)], [sx, sy0 + n * run, Z(h + n * rise + 36)], [sx, sy0, Z(h + 36)], [[0, 1], [7, 1], [7, 0], [0, 0]], null, O({ twoSided: true }));
      box("steel_el", 1000, sy0 + n * run, EL.deckH - 6, sx1, sy0 + n * run + 70, EL.deckH, {}, [], 32);
      decal("sign_subway", (sx0 + sx1) / 2 - 30, sy0 - 4, NY, 120, h + 96, h + 126, 0.5, { twoSided: true });
      for (const sx of [sx0 - 34, sx1 + 34]) { cylinder("steel_el", sx, sy0 - 4, 2.4, h, h + 96, 6, {}, false); cylinder("globe_green", sx, sy0 - 4, 9, h + 96, h + 106, 10, {}, true, 9); lights.push({ at: [sx, sy0 - 24, Z(h + 100)], brightness: 0.25, range: 260, color: "150 255 170" }); }
    }
    // tenements behind the line: brick, cornices, rows of windows, fire escapes, a wooden water tower
    const X0 = EAST.facade, X1 = EAST.back, blocks = [
      { y0: -Y1, y1: -520, h1: 610, mat: "plaster_white" }, { y0: -520, y1: 180, h1: 700, mat: "plaster_yellow" }, { y0: 180, y1: 760, h1: 590, mat: "plaster_teal" }, { y0: 760, y1: Y1, h1: 660, mat: "plaster_pink" },
    ];
    for (const d of blocks) {
      const b = { x0: X0, x1: X1, y0: d.y0, y1: d.y1, h0: h, h1: d.h1 };
      building(b, d.mat, "wsn");
      const cols = Math.round((d.y1 - d.y0) / 104), rows = Math.floor((d.h1 - h - 150) / 96);
      windows(b, "w", { cols, rows, w: 40, h: 64, first: 128, pitch: 96, mats: ["win_lit", "win_lit_b", "win_dark", "win_dark", "win_blind", "win_dark"], sill: "concrete_cap" });
      decal("shop_east", X0, (d.y0 + d.y1) / 2, NX, d.y1 - d.y0, h, h + 112, 1.5, {}, [0, 0, (d.y1 - d.y0) / 448, 1]);
      box("cornice", X0 - 8, d.y0, d.h1 - 20, X0 + 2, d.y1, d.h1 + 6, {}, ["x1"], 64);
      // balconies one above the other: a slab and a railing on every floor, washing over some railings
      const fy = d.y0 + (d.y1 - d.y0) * (0.3 + rnd() * 0.4), fw = 150;
      for (let r = 0; r < rows; r++) {
        const fh = h + 128 + r * 96 - 6;
        box("steel_dark", X0 - 30, fy - fw / 2, fh - 3, X0, fy + fw / 2, fh, {}, [], 24);
        wall("fire_rail", X0 - 30, fy + fw / 2, X0 - 30, fy - fw / 2, fh, fh + 34, NX, [0, 0, 3, 1], { twoSided: true });
        for (const e of [-1, 1]) wall("fire_rail", X0 - 30, fy + e * fw / 2, X0, fy + e * fw / 2, fh, fh + 34, e > 0 ? PY : NY, [0, 0, 0.6, 1], { twoSided: true });
        if (rnd() < 0.6) for (let q = 0; q < 3; q++) if (rnd() < 0.7) { const cy = fy - fw / 2 + 24 + q * 44, cw = 16 + rnd() * 14, ch = 14 + rnd() * 14; wall(pick(["cloth_a", "cloth_b", "cloth_c", "cloth_d"]), X0 - 31.5, cy, X0 - 31.5, cy + cw, fh + 33 - ch, fh + 33, NX, [0, 0, 1, 1], { twoSided: true }); }
      }
    }
    // blue water tanks on the roofs, a roof stair house, AC boxes under some windows
    for (const [tx, ty, th] of [[1250, -170, 700], [1300, -110, 700], [1260, -800, 610], [1270, 960, 660]]) { cylinder("tank_blue", tx, ty, 26, th, th + 46, 14, {}, false, 24); cylinder("tank_blue", tx, ty, 24, th + 46, th + 54, 14, {}, true, 9); }
    box("plaster_white", 1250, 380, 590, 1320, 460, 640, {}, ["bottom"], 128);
    for (const [y, hh] of [[-900, 300], [-640, 396], [-60, 300], [60, 492], [420, 396], [980, 300], [1100, 492]]) box("ac_unit", X0 - 14, y - 12, hh, X0, y + 12, hh + 16, {}, ["x1"], 28);
    // the ends of the line: it comes out from under a building in the north and goes into the hill in the south
    for (const s of [1, -1]) {
      const P = EL.portal, yF = s * EL.y, yB = s * (EL.y + P.depth), f = s > 0 ? NY : PY, mat = s > 0 ? "plaster_yellow" : "concrete_old";
      flat("road_old", x0, Math.min(s * Y1, yF), EAST.back, Math.max(s * Y1, yF), h, 256);
      wall("concrete", x0, s * Y1, x0, yF, 0, h, NX, 128);
      wall(mat, P.x0, yF, EL.cx - EL.width / 2 + 8, yF, s > 0 ? 0 : h, P.top, f, 128); wall(mat, EL.cx + EL.width / 2 - 8, yF, EAST.back, yF, h, P.top, f, 128);
      wall(mat, EL.cx - EL.width / 2 + 8, yF, EL.cx + EL.width / 2 - 8, yF, h, EL.girderH0, f, 128); wall(mat, EL.cx - EL.width / 2 + 8, yF, EL.cx + EL.width / 2 - 8, yF, EL.deckH + 156, P.top, f, 128);
      wall("portal_dark", EL.cx - EL.width / 2 + 8, yF + s * 2, EL.cx + EL.width / 2 - 8, yF + s * 2, EL.girderH0, EL.deckH + 156, f, [0, 0, 1, 1]);
      wall(mat, P.x0, yF, P.x0, yB, s > 0 ? 0 : h, P.top, NX, 128);
      box("cornice", P.x0 - 4, Math.min(yF, yF - s * 8), P.top - 18, EAST.back, Math.max(yF, yF - s * 8), P.top + 6, {}, [], 64);
      if (s > 0) windows({ x0: P.x0, x1: EAST.back, y0: yF, y1: yB, h0: EL.deckH + 156, h1: P.top }, "s", { cols: 7, rows: 0, w: 40, h: 64, first: 20, pitch: 96, mats: ["win_lit"] });
      else decal("g_tunnel", EL.cx, yF, f, 300, EL.deckH + 164, EL.deckH + 228, 1.5);
    }
    // street furniture under the tracks: barrels, tyres, pallets, a shopping trolley of crates
    for (const [x, y] of [[870, -560], [884, -534], [1000, 300]]) cylinder("barrel", x, y, 11, h, h + 34, 10, {}, true);
    for (let k = 0; k < 4; k++) cylinder("tyre", 990, -880, 13, h + k * 9, h + k * 9 + 8, 10, {}, true, 13);
    for (let k = 0; k < 3; k++) box("pallet", 840, 520, h + k * 6, 888, 560, h + k * 6 + 5, {}, ["bottom"], 48);
    lights.push({ at: [EL.cx, -600, Z(EL.girderH0 - 30)], brightness: 0.35, range: 520, color: "255 190 120" }, { at: [EL.cx, 0, Z(EL.girderH0 - 30)], brightness: 0.35, range: 520, color: "255 190 120" }, { at: [EL.cx, 600, Z(EL.girderH0 - 30)], brightness: 0.35, range: 520, color: "255 190 120" });
    // onlookers on the raised sidewalk at the fence, and under the tracks
    crowd.push({ a: [x0 + 34, -hy + 50], b: [x0 + 34, hy - 50], h, look: [-1, 0], group: 2, name: "east", sparse: 0.6 });
    crowd.push({ a: [x0 + 70, -620], b: [x0 + 70, 560], h, look: [-1, 0], group: 2, name: "east", sparse: 0.3 });
  }

  // =================================================================================================
  // north: a square of cobbles behind the wall, a row of colonial houses in bright colours with a church
  // in the middle, palms in front of them (owner 2026-10-02: "add a Brazil flair behind the other goal as well")
  // =================================================================================================
  G = "north";
  {
    const y0 = NORTH.yard, yF = NORTH.facade, yB = NORTH.back;
    flat("cobble", -hx - WEST_WALL.t, y0, EAST.x0, yF, 0, 128);
    wall("concrete", EAST.x0, yF, EAST.x0, hy + 16, 0, EAST.h, NX, 128);
    // the houses: plaster in one colour each, white corner strips and cornice, a dark plinth, arched windows
    // with shutters, doors, a balcony with an iron railing in front of the upper windows
    const houses = [{ x0: -900, x1: -700, h1: 330, mat: "plaster_blue" }, { x0: -700, x1: -520, h1: 292, mat: "plaster_yellow" }, { x0: -520, x1: -340, h1: 352, mat: "plaster_pink" }, { x0: -340, x1: -196, h1: 300, mat: "plaster_green" },
      { x0: 196, x1: 350, h1: 312, mat: "plaster_orange" }, { x0: 350, x1: 560, h1: 352, mat: "plaster_teal" }];
    for (const d of houses) {
      const b = { x0: d.x0, x1: d.x1, y0: yF, y1: yB, h1: d.h1, roof: "roof_tile" }, wdt = d.x1 - d.x0, cols = Math.max(2, Math.round(wdt / 64)), floors = d.h1 > 320 ? 2 : 1;
      building(b, d.mat, "swe");
      for (const x of [d.x0, d.x1 - 8]) box("trim_white", x, yF - 3, 0, x + 8, yF, d.h1, {}, ["bottom", "y1"], 64);
      box("trim_white", d.x0, yF - 7, d.h1 - 16, d.x1, yF + 1, d.h1 + 5, {}, ["y1"], 64);
      box("trim_stone", d.x0, yF - 2, 0, d.x1, yF, 22, {}, ["bottom", "y1"], 64);
      box("trim_white", d.x0, yF - 4, 146, d.x1, yF, 154, {}, ["y1"], 64);
      // ground floor: a door in one bay, windows in the others; upper floors: tall windows
      const door = Math.floor(rnd() * cols), step = (wdt - 40) / cols;
      for (let c = 0; c < cols; c++) { const cx = d.x0 + 20 + (c + 0.5) * step; if (c === door) decal(pick(["door_col_a", "door_col_b"]), cx, yF, NY, 50, 0, 112, 2.5); else decal(pick(["win_col_a", "win_col_b", "win_col_lit"]), cx, yF, NY, 42, 36, 114, 2.5); }
      for (let f = 0; f < floors; f++) {
        const fh = 168 + f * 118;
        for (let c = 0; c < cols; c++) decal(pick(["win_col_a", "win_col_b", "win_col_lit", "win_col_lit"]), d.x0 + 20 + (c + 0.5) * step, yF, NY, 42, fh, fh + 84, 2.5);
        // a balcony along the first upper floor
        if (f === 0) { box("trim_stone", d.x0 + 14, yF - 26, fh - 6, d.x1 - 14, yF, fh - 1, {}, ["y1"], 48); wall("fire_rail", d.x0 + 14, yF - 26, d.x1 - 14, yF - 26, fh - 1, fh + 32, NY, [0, 0, wdt / 52, 1], { twoSided: true });
          for (const e of [d.x0 + 14, d.x1 - 14]) wall("fire_rail", e, yF - 26, e, yF, fh - 1, fh + 32, e < (d.x0 + d.x1) / 2 ? NX : PX, [0, 0, 0.5, 1], { twoSided: true });
          crowd.push({ a: [d.x0 + 30, yF - 13], b: [d.x1 - 30, yF - 13], h: fh - 1, look: [0, -1], group: 2, name: "end_red", sparse: 0.5 }); }
      }
    }
    // the church: a white front with ochre strips between two towers, a stepped gable with a cross, a wide door
    {
      const cf = yF - 24, tw = 86, nave = { x0: -110, x1: 110, y0: cf, y1: yB, h1: 430, roof: "roof_tile" };
      building(nave, "plaster_white", "s");
      box("plaster_white", -78, cf, 430, 78, cf + 30, 474, {}, ["bottom"], 128); box("plaster_white", -44, cf, 474, 44, cf + 30, 508, {}, ["bottom"], 128);
      for (const [x0, x1, hA, hB] of [[-110, 110, 424, 434], [-78, 78, 470, 478], [-44, 44, 504, 512]]) box("plaster_ochre", x0 - 4, cf - 5, hA, x1 + 4, cf + 2, hB, {}, ["y1"], 64);
      wall("cross", -22, cf + 14, 22, cf + 14, 512, 578, NY, [0, 0, 1, 1], { twoSided: true });
      decal("door_church", 0, cf, NY, 104, 0, 176, 2.5); decal("win_rose", 0, cf, NY, 74, 262, 336, 2.5);
      for (const x of [-70, 70]) decal("win_col_b", x, cf, NY, 40, 190, 270, 2.5);
      for (const s of [-1, 1]) {
        const t = { x0: s > 0 ? 110 : -110 - tw, x1: s > 0 ? 110 + tw : -110, y0: cf - 10, y1: cf + 96, h1: 560, roof: "roof_tile" }, mx = (t.x0 + t.x1) / 2, my = (t.y0 + t.y1) / 2;
        building(t, "plaster_white", "swen");
        for (const x of [t.x0, t.x1 - 9]) box("plaster_ochre", x, t.y0 - 3, 0, x + 9, t.y0, 560, {}, ["bottom", "y1"], 64);
        for (const hh of [300, 420, 552]) box("plaster_ochre", t.x0 - 4, t.y0 - 6, hh, t.x1 + 4, t.y0 + 2, hh + 10, {}, ["y1"], 64);
        decal("win_bell", mx, t.y0, NY, 40, 448, 530, 2.5); decal("win_bell", s > 0 ? t.x1 : t.x0, my, s > 0 ? PX : NX, 40, 448, 530, 2.5); decal("win_bell", s > 0 ? t.x0 : t.x1, my, s > 0 ? NX : PX, 40, 448, 530, 2.5);
        decal("win_col_b", mx, t.y0, NY, 34, 200, 268, 2.5);
        // the tower's cap: four tiled faces up to a point, a ball on it
        const apex = [mx, my, Z(672)], ring = [[t.x0 - 4, t.y0 - 4], [t.x1 + 4, t.y0 - 4], [t.x1 + 4, t.y1 + 4], [t.x0 - 4, t.y1 + 4]];
        for (let k = 0; k < 4; k++) { const a = ring[k], c = ring[(k + 1) % 4]; scene.poly(M("roof_tile"), [[a[0], a[1], Z(562)], [c[0], c[1], Z(562)], apex], [[0, 1], [1, 1], [0.5, 0]], [(a[0] + c[0]) / 2 - mx, (a[1] + c[1]) / 2 - my, 0.5], O()); }
        cylinder("plaster_ochre", mx, my, 5, 668, 682, 8, {}, true, 3);
      }
      // the steps in front of the door, people sitting on them
      for (let k = 0; k < 4; k++) box("trim_stone", -128, cf - 12 - (4 - k) * 12, 0, 128, cf - 12 - (3 - k) * 12, (k + 1) * 7, {}, ["bottom"], 48);
      box("trim_stone", -128, cf - 12, 0, 128, cf, 28, {}, ["bottom", "y1"], 48);
      crowd.push({ a: [-110, cf - 30], b: [-58, cf - 30], h: 21, look: [0, -1], group: 2, name: "end_red", sparse: 0.8 }, { a: [58, cf - 30], b: [110, cf - 30], h: 21, look: [0, -1], group: 2, name: "end_red", sparse: 0.8 });
    }
    // palms in the square, a bench, bins, crates
    for (const [x, y, hh, turn] of [[-610, 1090, 300, 0.2], [-300, 1060, 330, 1.1], [300, 1060, 320, 2.3], [520, 1100, 290, 0.7]]) palm(x, y, hh, turn);
    for (const [x, y] of [[-420, 1150], [-350, 1160]]) { cylinder("bin", x, y, 13, 0, 36, 10, {}, false); cylinder("bin_lid", x, y, 14.5, 36, 40, 10, {}, true, 12); }
    for (const [x, y, n] of [[420, 1140, 3], [470, 1150, 2]]) for (let k = 0; k < n; k++) box("crate_milk", x - 14, y - 14, k * 13, x + 14, y + 14, k * 13 + 12, {}, ["bottom"], 28);
    lights.push({ at: [-560, 1140, Z(190)], brightness: 0.4, range: 480, color: "255 200 140" }, { at: [560, 1140, Z(190)], brightness: 0.4, range: 480, color: "255 200 140" }, { at: [0, 1130, Z(210)], brightness: 0.45, range: 420, color: "255 214 160" });
  }

  // =================================================================================================
  // south (blue end): the yard, houses on three terraces up the hill, the tiled stairway in the middle
  // =================================================================================================
  G = "south";
  {
    const y0 = SOUTH.yard, T = SOUTH.tiers, sh = SOUTH.stairHalf;
    flat("road_old", -hx - WEST_WALL.t, T[0].y, EAST.x0, y0, 0, 256);
    const plasters = ["plaster_yellow", "plaster_teal", "plaster_pink", "plaster_orange", "plaster_green", "plaster_white", "plaster_blue", "brick_hollow"];
    const RAY_PLASTER = ["plaster_yellow", "plaster_orange", "plaster_coral", "plaster_pink", "plaster_blue", "plaster_teal", "plaster_green"];
    for (let t = 0; t < T.length; t++) {
      const front = T[t].y, base = T[t].h, backY = t + 1 < T.length ? T[t + 1].y : SOUTH.back, nextBase = t + 1 < T.length ? T[t + 1].h : base + 150;
      // the terrace's retaining wall behind this row and its ground
      if (t) { wall("concrete_old", -1000, front + 0, 560, front + 0, T[t - 1].h, base, PY, 128); flat("ground_dirt", -1000, front - 40, 560, front, base, 128); }
      let x = -960 + rnd() * 30;
      while (x < 500) {                                                      // the line passes east of the houses
        const w = 120 + rnd() * 110, floors = 1 + (rnd() < 0.55 ? 1 : 0) + (t > 0 && rnd() < 0.3 ? 1 : 0), hh = base + 20 + floors * 92 + rnd() * 16;
        // the court's rays go on up the hill: a house has the colour of the ray it stands in (now and then its own)
        const mat = rnd() < 0.78 ? RAY_PLASTER[rayOf(x + w / 2, front - 90) % RAY_PLASTER.length] : pick(plasters);
        const x1 = Math.min(x + w, 540);
        if (x1 <= -sh - 6 || x >= sh + 6) {                                   // keep the stairway free
          const b = { x0: x, x1, y0: Math.max(backY, front - 150 - rnd() * 40), y1: front - 40 - rnd() * 12, h0: base, h1: hh, roof: rnd() < 0.5 ? "roof_tin" : "roof_slab" };
          building(b, mat, "nwe");
          if (floors > 1 && rnd() < 0.6) { const [ax, ay, bx, by, f] = [b.x1, b.y1, b.x0, b.y1, PY]; wall("brick_hollow", ax, ay + 1.2, bx, by + 1.2, base + 20 + (floors - 1) * 92, hh, f, 96); }   // unfinished top floor
          windows(b, "n", { cols: Math.max(1, Math.round((x1 - x) / 76)), rows: floors, w: 30, h: 38, first: 46, pitch: 92, margin: 18, mats: ["win_fav_lit", "win_fav_dark", "win_fav_grill", "win_fav_lit_b"], skip: (c, r) => r === 0 && c === 0 });
          decal(pick(["door_fav_a", "door_fav_b", "door_fav_c"]), x + 18 + 16, b.y1, PY, 32, base, base + 76, 2.5);
          if (rnd() < 0.45) { cylinder("tank_blue", (x + x1) / 2, b.y1 - 40, 20, hh, hh + 34, 12, {}, false, 18); cylinder("tank_blue", (x + x1) / 2, b.y1 - 40, 18, hh + 34, hh + 40, 12, {}, true, 7); }
          if (rnd() < 0.35) scene.quad(M("dish"), [x + 20, b.y1 - 6, Z(hh + 4)], [x + 44, b.y1 - 6, Z(hh + 4)], [x + 44, b.y1 - 16, Z(hh + 28)], [x + 20, b.y1 - 16, Z(hh + 28)], [[0, 1], [1, 1], [1, 0], [0, 0]], PY, O({ twoSided: true }));
          if (rnd() < 0.5) decal(pick(["g_fav_a", "g_fav_b", "g_fav_c", "g_fav_d"]), (x + x1) / 2, b.y1, PY, Math.min(110, x1 - x - 20), base + 4, base + 44, 3.4);
          // washing lines from this house towards the next
          if (floors > 1 && rnd() < 0.5) { const lh = base + 20 + 92 + 40, ya = b.y1 + 2; cable("cable", [x + 8, ya, Z(lh)], [x1 + 60, ya + 4, Z(lh - 6)], 0.8); for (let q = 0; q < 5; q++) { const lx = x + 20 + q * ((x1 - x + 30) / 5), lw = 14 + rnd() * 12, lh2 = 16 + rnd() * 16; scene.quad(M(pick(["cloth_a", "cloth_b", "cloth_c", "cloth_d"])), [lx, ya + 1, Z(lh - 1 - lh2)], [lx + lw, ya + 1, Z(lh - 2 - lh2)], [lx + lw, ya + 1, Z(lh - 2)], [lx, ya + 1, Z(lh - 1)], [[0, 1], [1, 1], [1, 0], [0, 0]], PY, O({ twoSided: true })); } }
          // people on the flat roofs of the first row
          if (t === 0 && b.roof === "roof_slab" && rnd() < 0.8) crowd.push({ a: [x + 16, b.y1 - 14], b: [x1 - 16, b.y1 - 14], h: hh, look: [0, 1], group: 2, name: "end_blue", sparse: 0.55 });
        }
        x = x1 + 2 + rnd() * 10;
      }
    }
    // the tiled stairway (mosaic risers, like the steps of Rio), people sitting on it
    { const yTop = SOUTH.back, yBot = T[0].y + 6, steps = 30, rise = 450 / steps, run = (yBot - yTop) / steps;
      for (let k = 0; k < steps; k++) {
        const ya = yBot - k * run, yb = ya - run, hA = k * rise, hB = (k + 1) * rise;
        wall(k % 3 === 0 ? "stair_tiles_b" : "stair_tiles", -sh, ya, sh, ya, hA, hB, PY, [k * 0.37, 0, k * 0.37 + (2 * sh) / 64, 1]);
        flat("stair_tread", -sh, yb, sh, ya, hB, 64);
        if (k % 4 === 2 && k < 22) crowd.push({ a: [-sh + 14, (ya + yb) / 2], b: [sh - 14, (ya + yb) / 2], h: hB, look: [0, 1], group: 2, name: "end_blue", sparse: 0.7 });
      }
      for (const e of [-1, 1]) scene.quad(M("stair_tiles_b"), [e * sh, yBot, Z(0)], [e * sh, yTop, Z(450)], [e * sh, yTop, Z(474)], [e * sh, yBot, Z(24)], [[0, 1], [9, 1], [9, 0], [0, 0]], null, O({ twoSided: true })); }
    // a kiosk-bar in the yard with a striped roof, plastic chairs, a football table
    box("plaster_white", 380, -1150, 0, 520, -1060, 96, {}, ["bottom"], 128); decal("bar_front", 450, -1060, PY, 140, 0, 96, 1.2);
    palm(250, -1090, 250, 0.4);
    scene.quad(M("awning_yellow"), [370, -1160, Z(104)], [530, -1160, Z(104)], [530, -1010, Z(88)], [370, -1010, Z(88)], [[0, 0], [2.5, 0], [2.5, 1], [0, 1]], UP, O({ twoSided: true }));
    for (const [x, y] of [[330, -1030], [560, -1040], [585, -1000]]) { box("chair_plastic", x - 9, y - 9, 16, x + 9, y + 9, 18, {}, [], 18); for (const [dx, dy] of [[-8, -8], [8, -8], [8, 8], [-8, 8]]) box("chair_plastic", x + dx - 1, y + dy - 1, 0, x + dx + 1, y + dy + 1, 16, {}, ["bottom", "top"], 18); box("chair_plastic", x - 9, y - 9, 18, x + 9, y - 7, 36, {}, [], 18); }
    for (const [x, y] of [[-420, -1130], [-470, -1080], [-390, -1070]]) cylinder("barrel_blue", x, y, 11, 0, 34, 10, {}, true);
    lights.push({ at: [450, -1020, Z(86)], brightness: 0.4, range: 360, color: "255 214 150" }, { at: [-520, -1150, Z(170)], brightness: 0.4, range: 480, color: "255 200 140" });
    crowd.push({ a: [300, -1010], b: [600, -1010], h: 0, look: [0, 1], group: 2, name: "end_blue", sparse: 0.3 });
  }

  // =================================================================================================
  // string lights across the court, cables along the street
  // =================================================================================================
  G = "props";
  {
    const xa = LAMPS.x, xb = EL.cx - EL.width / 2 - 5;
    for (const y of STRINGS.ys) {
      const n = Math.round((xb - xa) / STRINGS.bulbEvery), pt = (t) => [xa + (xb - xa) * t, y + Math.sin(t * Math.PI) * 6, Z(STRINGS.h0 + (STRINGS.h1 - STRINGS.h0) * t - Math.sin(t * Math.PI) * STRINGS.sag)];
      for (let k = 0; k < n; k++) cable("cable", pt(k / n), pt((k + 1) / n), 0.7);
      for (let k = 1; k < n; k++) { const p = pt(k / n), r = 2.6; for (const [dx, dy] of [[r, 0], [0, r]]) scene.quad(M("bulb"), [p[0] - dx, p[1] - dy, p[2] - 5.4], [p[0] + dx, p[1] + dy, p[2] - 5.4], [p[0] + dx, p[1] + dy, p[2] - 0.2], [p[0] - dx, p[1] - dy, p[2] - 0.2], [[0, 1], [1, 1], [1, 0], [0, 0]], null, O({ twoSided: true })); }
      for (const t of [0.25, 0.5, 0.75]) { const p = pt(t); lights.push({ at: [p[0], p[1], p[2] - 14], brightness: 0.22, range: 420, color: "255 206 140" }); }
    }
    // power lines along the west street on leaning poles, shoes over a wire
    const poles = [-1050, -350, 350, 1050].map((y) => [WEST.street1 + 14, y]);
    for (const [x, y] of poles) { cylinder("wood_pole", x, y, 4.5, 0, 330, 8, {}, false, 3.4); box("wood_pole", x - 2, y - 30, 300, x + 2, y + 30, 304, {}, [], 32); cylinder("transformer", x + 9, y, 8, 250, 280, 8, {}, true); }
    for (let k = 0; k + 1 < poles.length; k++) for (const o of [-26, 0, 26]) { const a = poles[k], b = poles[k + 1], n = 8, p = (t) => [a[0], a[1] + o + (b[1] - a[1]) * t, Z(304 - Math.sin(t * Math.PI) * (20 + Math.abs(o) * 0.2))]; for (let q = 0; q < n; q++) cable("cable", p(q / n), p((q + 1) / n), 0.7); }
  }
  // sneakers thrown over a wire: over the street of shops and on a string of lights
  for (const [x, y, h] of [[WEST.street1 + 14, 0, 284], [-160, 240 + 5, 392]]) wall("sneakers", x, y - 14, x, y + 14, h - 30, h, PX, [0, 0, 1, 1], { twoSided: true });
  // =================================================================================================
  // the street ends: the street of shops meets a cross street in the north (a brick house closes the view) and
  // runs into the first houses of the hill in the south; the gaps beside the tunnel mouths
  // =================================================================================================
  G = "west";
  {
    const Y1 = WEST.y, curb = 5;
    // north: cross street, far sidewalk, a four-storey brick house
    flat("road", WEST.back, Y1, -900, Y1 + 140, -curb, 256);
    box("curb", WEST.back, Y1 + 140, -curb, -900, Y1 + 146, 0.2, {}, ["bottom", "x0", "x1", "y1"], 64); flat("sidewalk", WEST.back, Y1 + 146, -900, Y1 + 160, 0, 128);
    wall("curb", WEST.far1, Y1, WEST.street1, Y1, -curb, 0, PY, 64); wall("curb", -900, Y1, -900, Y1 + 140, -curb, 0, NX, 64);
    for (let k = 0; k < 6; k++) ground("road_zebra", WEST.street1 + 30 + k * 34, Y1 + 30, 20, 50, [1, 0], -curb + 0.3);
    const b = { x0: WEST.back, x1: -900, y0: Y1 + 160, y1: NORTH.back + 60, h1: 560 };
    building(b, "plaster_green", "s");
    windows(b, "s", { cols: 5, rows: 3, w: 46, h: 84, first: 168, pitch: 128, mats: ["win_col_a", "win_col_b", "win_col_lit"] });
    decal("shop_east", (b.x0 + b.x1) / 2, b.y0, NY, b.x1 - b.x0, 0, 112, 1.5, {}, [0, 0, (b.x1 - b.x0) / 448, 1]);
    box("cornice", b.x0, b.y0 - 8, b.h1 - 22, b.x1, b.y0 + 2, b.h1 + 6, {}, ["y1"], 64);
    { const fx = -1240, fw = 150; for (let r = 0; r < 3; r++) { const fh = 168 + r * 128 - 6; box("steel_dark", fx - fw / 2, b.y0 - 30, fh - 3, fx + fw / 2, b.y0, fh, {}, [], 24); wall("fire_rail", fx - fw / 2, b.y0 - 30, fx + fw / 2, b.y0 - 30, fh, fh + 34, NY, [0, 0, 3, 1], { twoSided: true }); } }
    wall("plaster_orange", WEST.back, Y1 + 160, WEST.back, Y1, 0, 286, PX, 128);                   // the yard wall where the cross street leaves
    for (const y of [-520, 80, 900]) palm(WEST.far1 + 26, y, 270 + (y % 7) * 4, y * 0.01);
    lights.push({ at: [-1160, Y1 + 90, Z(170)], brightness: 0.4, range: 420, color: "255 200 140" });
    // south: the hill's first houses stand across the street
    flat("sidewalk", WEST.back, -Y1 - 24, -960, -Y1, 0, 128); wall("curb", WEST.street1, -Y1, -960, -Y1, -curb, 0, NY, 64);
    const lower = { x0: WEST.back, x1: -1190, y0: -Y1 - 200, y1: -Y1 - 24, h1: 214, roof: "roof_slab" }, mid = { x0: -1188, x1: -962, y0: -Y1 - 190, y1: -Y1 - 30, h1: 122, roof: "roof_tin" }, upper = { x0: -1150, x1: -962, y0: -Y1 - 190, y1: -Y1 - 70, h0: 122, h1: 300, roof: "roof_slab" };
    building(lower, "plaster_teal", "ne"); building(mid, "plaster_orange", "n"); building(upper, "brick_hollow", "nw", 96);
    windows(lower, "n", { cols: 3, rows: 2, w: 30, h: 38, first: 46, pitch: 92, margin: 18, mats: ["win_fav_lit", "win_fav_dark", "win_fav_grill"], skip: (c, r) => r === 0 && c === 1 });
    windows(mid, "n", { cols: 3, rows: 1, w: 30, h: 38, first: 46, pitch: 92, margin: 18, mats: ["win_fav_lit_b", "win_fav_dark", "win_fav_grill"], skip: (c) => c === 0 });
    windows(upper, "n", { cols: 2, rows: 2, w: 30, h: 38, first: 34, pitch: 86, margin: 18, mats: ["win_fav_lit", "win_fav_dark"] });
    decal("door_fav_b", (lower.x0 + lower.x1) / 2, lower.y1, PY, 32, 0, 76, 0.6); decal("door_fav_c", mid.x1 - 30, mid.y1, PY, 32, 0, 76, 0.6);
    decal("g_fav_b", -1300, lower.y1, PY, 100, 100, 140, 0.7);
    cylinder("tank_blue", -1050, -Y1 - 130, 20, 300, 334, 12, {}, false, 18); cylinder("tank_blue", -1050, -Y1 - 130, 18, 334, 340, 12, {}, true, 7);
    lights.push({ at: [-1080, -Y1 + 60, Z(150)], brightness: 0.4, range: 420, color: "255 200 140" });
  }
  G = "east";
  for (const s of [1, -1]) wall(s > 0 ? "plaster_yellow" : "concrete_old", EAST.back, s > 0 ? EAST.y : -EL.y, EAST.back, s > 0 ? EL.y : -EAST.y, EAST.h, EL.portal.top, NX, 128);
  // =================================================================================================
  // far away: painted cut-outs behind the houses (they keep their own brightness and cast no shadow)
  // =================================================================================================
  G = "far";
  {
    const X = FAR.x;
    // the hill in two halves (the viewer looks south: east is on the left)
    wall("far_hill_a", 0, FAR.south, X, FAR.south, -40, FAR.hillTop, PY, [0, 0, 1, 1]); wall("far_hill_b", -X, FAR.south, 0, FAR.south, -40, FAR.hillTop, PY, [0, 0, 1, 1]);
    wall("far_bay", -X, FAR.north, 700, FAR.north, -40, FAR.bayTop, NY, [0, 0, 1, 1]);
    wall("far_roofs", -X, FAR.south, -X, FAR.north, -40, FAR.roofsTop, PX, [0, 0, 2, 1]);
  }
  // =================================================================================================
  // two vehicles: an old two-tone van in the street of shops, a yellow cab under the tracks
  // =================================================================================================
  {
    // a car along y at (cx, cy), base height h0: boxes, window bands, wheels as discs
    const wheel = (x, y, h, side) => { const r = 13, p = (a) => [x, y + Math.cos(a) * r, Z(h + r + Math.sin(a) * r)], ring = [...Array(14).keys()].map((k) => p(k * Math.PI / 7)); scene.poly(M("wheel"), ring, ring.map((q, k) => [0.5 + Math.cos(k * Math.PI / 7) * 0.5, 0.5 - Math.sin(k * Math.PI / 7) * 0.5]), [side, 0, 0], O({ twoSided: true })); };
    const car = (cx, cy, h0, o) => {
      box(o.low, cx - o.w / 2, cy - o.len / 2, h0 + 9, cx + o.w / 2, cy + o.len / 2, h0 + o.belt, {}, [], 64);
      box(o.up, cx - o.w / 2 + 2, cy + o.cab[0], h0 + o.belt, cx + o.w / 2 - 2, cy + o.cab[1], h0 + o.roof, {}, ["bottom"], 64);
      box("car_dark", cx - o.w / 2 + 3, cy - o.len / 2 + 6, h0 + 3, cx + o.w / 2 - 3, cy + o.len / 2 - 6, h0 + 9, {}, [], 32);
      for (const s of [1, -1]) {
        const x = cx + s * (o.w / 2 - 2 + 0.4);
        wall(o.glass, x, cy + o.cab[0] + 4, x, cy + o.cab[1] - 4, h0 + o.belt + 3, h0 + o.roof - 5, [s, 0, 0], [0, 0, 1, 1]);
        for (const wy of o.wheels) wheel(cx + s * (o.w / 2 + 0.5), cy + wy, h0, s);
        if (o.stripe) wall(o.stripe, cx + s * (o.w / 2 + 0.3), cy - o.len / 2 + 8, cx + s * (o.w / 2 + 0.3), cy + o.len / 2 - 8, h0 + o.belt - 9, h0 + o.belt - 2, [s, 0, 0], [0, 0, 12, 1]);
      }
      decal(o.front, cx, cy + o.len / 2, PY, o.w - 2, h0 + 9, h0 + (o.frontH ?? o.belt), 0.3); decal(o.back, cx, cy - o.len / 2, NY, o.w - 2, h0 + 9, h0 + (o.frontH ?? o.belt), 0.3);
      for (const [yy, f] of [[cy + o.cab[1], PY], [cy + o.cab[0], NY]]) if (!o.frontH) decal(o.glass, cx, yy, f, o.w - 12, h0 + o.belt + 3, h0 + o.roof - 5, 0.3);
      for (const e of [1, -1]) box("car_dark", cx - o.w / 2 - 1, cy + e * (o.len / 2 + 1), h0 + 9, cx + o.w / 2 + 1, cy + e * (o.len / 2 + 4), h0 + 15, {}, [], 32);
    };
    G = "props";
    car(-960, -260, -5, { w: 70, len: 176, belt: 46, roof: 80, cab: [-86, 86], low: "van_low", up: "van_up", glass: "van_glass", front: "van_front", back: "van_back", frontH: 80, wheels: [-56, 56] });
    car(820, -150, EAST.h, { w: 70, len: 190, belt: 34, roof: 56, cab: [-46, 30], low: "taxi_yellow", up: "taxi_yellow", glass: "taxi_glass", front: "taxi_front", back: "taxi_back", stripe: "taxi_checker", wheels: [-60, 60] });
    box("taxi_sign", 808, -160, EAST.h + 56, 832, -152, EAST.h + 63, {}, ["bottom"], 24);
  }
  // the rows at the two ends belong to the end sections of the crowd models (one bone set each)
  for (const r of crowd) r.group = r.name === "end_red" ? 0 : r.name === "end_blue" ? 1 : 2;
  return { scene, crowd, lights };
}
export { FLOOR };
