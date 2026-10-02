// SoccerMod gym: every face of the map, grouped by material and by model (opts.group).
// layout.mjs holds the numbers. Collision is a handful of plain thick boxes (physOnly: in the
// collision mesh only), so the ball and the players meet simple, solid shapes.
import { FLOOR, Z, COURT, BOARD, MESH, NET_TOP, GOAL, HALL, STAND, standRow, LAMPS, courtLines, AREA, GLASS, TERRACE, CITY, ART } from "./layout.mjs";
import { Scene } from "../arena/lib/mesh.mjs";

const UP = [0, 0, 1], DOWN = [0, 0, -1], PX = [1, 0, 0], NX = [-1, 0, 0], PY = [0, 1, 0], NY = [0, -1, 0];
const M = (name) => `materials/soccermod_gym/${name}.vmat`;

export function buildGym() {
  const scene = new Scene();
  const crowd = [];     // rows of fans: { a: [x, y], b: [x, y], h, look: [nx, ny], group: 2 (mixed), name }
  const lights = [];    // baked lights: { at: [x, y, z], brightness, range, color? }
  let G = "hall";       // the model the next faces go into
  const O = (o = {}) => ({ group: G, ...o });

  // ---- helpers -------------------------------------------------------------------------------------
  // box from two corners; heights are above the floor. skip: "x0" "x1" "y0" "y1" "top" "bottom"
  const SIDE = { s0: "y0", s1: "x1", s2: "y1", s3: "x0", top: "top", bottom: "bottom" };
  const box = (mat, xa, ya, h0, xb, yb, h1, o = {}, skip = [], uv = 64) => {
    const x0 = Math.min(xa, xb), x1 = Math.max(xa, xb), y0 = Math.min(ya, yb), y1 = Math.max(ya, yb);
    const b = [[x0, y0, Z(h0)], [x1, y0, Z(h0)], [x1, y1, Z(h0)], [x0, y1, Z(h0)]], t = b.map((p) => [p[0], p[1], Z(h1)]);
    scene.prism(M(mat), b, t, uv, O(o), Object.keys(SIDE).filter((k) => skip.includes(SIDE[k])));
  };
  // collision only: a solid box that is not drawn
  const solid = (xa, ya, h0, xb, yb, h1) => box("collide", xa, ya, h0, xb, yb, h1, { solid: true, physOnly: true });
  const flat = (mat, x0, y0, x1, y1, h, tile, facing = UP, opts = {}) =>
    scene.quad(M(mat), [x0, y0, Z(h)], [x1, y0, Z(h)], [x1, y1, Z(h)], [x0, y1, Z(h)], [[x0 / tile, -y0 / tile], [x1 / tile, -y0 / tile], [x1 / tile, -y1 / tile], [x0 / tile, -y1 / tile]], facing, O(opts));
  // vertical wall on a line of constant x or constant y; uv = [u0, v0, u1, v1] (v0 = the top) or a
  // tile size in units. Pictures: pass u0 > u1 where the wall is seen from the other side.
  const tiled = (a0, a1, h0, h1, t) => [a0 / t, -h1 / t, a1 / t, -h0 / t];
  const wallX = (mat, x, y0, y1, h0, h1, facing, uv = 128, opts = {}) => {
    const [u0, v0, u1, v1] = Array.isArray(uv) ? uv : tiled(y0, y1, h0, h1, uv);
    scene.quad(M(mat), [x, y0, Z(h0)], [x, y1, Z(h0)], [x, y1, Z(h1)], [x, y0, Z(h1)], [[u0, v1], [u1, v1], [u1, v0], [u0, v0]], facing, O(opts));
  };
  const wallY = (mat, y, x0, x1, h0, h1, facing, uv = 128, opts = {}) => {
    const [u0, v0, u1, v1] = Array.isArray(uv) ? uv : tiled(x0, x1, h0, h1, uv);
    scene.quad(M(mat), [x0, y, Z(h0)], [x1, y, Z(h0)], [x1, y, Z(h1)], [x0, y, Z(h1)], [[u0, v1], [u1, v1], [u1, v0], [u0, v0]], facing, O(opts));
  };
  const { hx, hy, lid } = COURT, HX = HALL.x, CEIL = HALL.ceiling, TOP = lid + 44;
  const mouth = GOAL.half + GOAL.post, bar = GOAL.height + GOAL.post;

  // =================================================================================================
  // the floor: parquet with the painted lines of three sports, a strip of linoleum under the stands
  // =================================================================================================
  G = "floor";
  {
    const cage = hx + BOARD.t;
    flat("parquet", -cage, -hy, cage, hy, 0, 256);
    for (const s of [1, -1]) {
      const y0 = s * hy, y1 = s * (hy + GOAL.depth);
      flat("parquet", -mouth, Math.min(y0, y1), mouth, Math.max(y0, y1), 0, 256);
      flat("lino", s > 0 ? cage : -HX, -hy, s > 0 ? HX : -cage, hy, 0, 128);
    }
    // painted areas under the lines: the goal areas in the teams' colours, the emblem on the centre spot
    const fan = (mat, cx, cy, r, a0, a1, seg, h, uv) => { for (let k = 0; k < seg; k++) { const p = (t) => [cx + Math.cos(t) * r, cy + Math.sin(t) * r], tri = [[cx, cy], p(a0 + (a1 - a0) * k / seg), p(a0 + (a1 - a0) * (k + 1) / seg)]; scene.poly(M(mat), tri.map(([x, y]) => [x, y, Z(h)]), tri.map(uv), UP, O()); } };
    // The painting: big shapes of stained parquet across the whole court (the boards and their grain run on
    // through the colours). Every shape is cut at the cage; the second layer lies on the first.
    {
      const inside = [[1, 0, hx], [-1, 0, hx], [0, 1, hy], [0, -1, hy]];   // nx, ny, d: keep nx * x + ny * y <= d
      const clip = (poly) => { let p = poly; for (const [nx, ny, d] of inside) { const out = []; for (let i = 0; i < p.length; i++) { const a = p[i], b = p[(i + 1) % p.length], da = nx * a[0] + ny * a[1] - d, db = nx * b[0] + ny * b[1] - d; if (da <= 0) out.push(a); if ((da < 0 && db > 0) || (da > 0 && db < 0)) { const t = da / (da - db); out.push([a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t]); } } p = out; if (p.length < 3) return null; } return p; };
      const paint = (colour, layer, poly) => { const p = clip(poly); if (p) scene.poly(M(`wood_${colour}`), p.map(([x, y]) => [x, y, Z(0.08 + layer * 0.1)]), p.map(([x, y]) => [x / 256, -y / 256]), UP, O()); };
      for (const shape of ART) {
        const kind = shape[0], colour = shape[shape.length - 2], layer = shape[shape.length - 1];
        if (kind === "disc") { const [, cx, cy, r] = shape, seg = r > 100 ? 72 : 20; for (let k = 0; k < seg; k++) { const a0 = k * 2 * Math.PI / seg, a1 = (k + 1) * 2 * Math.PI / seg; paint(colour, layer, [[cx, cy], [cx + Math.cos(a0) * r, cy + Math.sin(a0) * r], [cx + Math.cos(a1) * r, cy + Math.sin(a1) * r]]); } }
        else if (kind === "ring") { const [, cx, cy, r0, r1, a0, a1] = shape, seg = 40; for (let k = 0; k < seg; k++) { const t0 = a0 + (a1 - a0) * k / seg, t1 = a0 + (a1 - a0) * (k + 1) / seg, q = (r, t) => [cx + Math.cos(t) * r, cy + Math.sin(t) * r]; paint(colour, layer, [q(r0, t0), q(r1, t0), q(r1, t1), q(r0, t1)]); } }
        else if (kind === "band") { const [, ax, ay, bx, by, w] = shape, l = Math.hypot(bx - ax, by - ay), nx = -(by - ay) / l * w / 2, ny = (bx - ax) / l * w / 2; paint(colour, layer, [[ax - nx, ay - ny], [bx - nx, by - ny], [bx + nx, by + ny], [ax + nx, ay + ny]]); }
        else paint(colour, layer, shape[1]);
      }
    }
    // on the painting: the two goal areas in white stain, the emblem on the centre spot, the game's white lines
    for (const s of [1, -1]) fan("wood_white", 0, s * AREA.y, AREA.r, s > 0 ? Math.PI : 0, s > 0 ? 2 * Math.PI : Math.PI, 28, 0.3, ([x, y]) => [x / 256, -y / 256]);
    fan("emblem", 0, 0, AREA.emblem, 0, 2 * Math.PI, 48, 0.3, ([x, y]) => [0.5 + x / (2 * AREA.emblem), 0.5 - y / (2 * AREA.emblem)]);
    const lines = courtLines();
    for (const poly of lines.white) scene.poly(M("line_white"), poly.map(([x, y]) => [x, y, Z(0.45)]), poly.map(([x, y]) => [x / 32, -y / 32]), UP, O());
    solid(-HX - 40, -hy - 160, -64, HX + 40, hy + 160, 0);
  }

  // =================================================================================================
  // the cage: kick boards, steel mesh and a net on the long sides, a net under the beams; the goals
  // =================================================================================================
  G = "cage";
  for (const s of [1, -1]) {
    const xi = s * hx, xo = s * (hx + BOARD.t), xm = (xi + xo) / 2, look = s > 0 ? NX : PX, away = s > 0 ? PX : NX, n = (2 * hy) / 240;
    // lettering reads left to right from where it is seen
    wallX("kickboard", xi, -hy, hy, 0, BOARD.h, look, s > 0 ? [n, 0, 0, 1] : [0, 0, n, 1]);
    wallX("kickboard", xo, -hy, hy, 0, BOARD.h, away, s > 0 ? [0, 0, n, 1] : [n, 0, 0, 1]);
    box("steel_dark", xi - s, -hy, BOARD.h, xo + s, hy, BOARD.h + 3, {}, ["bottom", "y0", "y1"]);
    wallX("mesh", xm, -hy, hy, BOARD.h + 3, MESH.h1, look, [0, 0, (2 * hy) / 48, (MESH.h1 - BOARD.h - 3) / 48], { twoSided: true });
    wallX("net", xm, -hy, hy, MESH.h1, NET_TOP, look, [0, 0, (2 * hy) / 128, (NET_TOP - MESH.h1) / 128], { twoSided: true });
    for (let y = -hy; y <= hy + 1; y += MESH.bay) { const ya = Math.max(-hy, y - 2.5), yb = Math.min(hy, y + 2.5); box("steel_dark", xi + s * 0.5, ya, BOARD.h + 3, xo - s * 0.5, yb, NET_TOP + 3, {}, ["bottom"]); }
    for (const h of [MESH.h1, (BOARD.h + MESH.h1) / 2, NET_TOP]) box("steel_dark", xi + s, -hy, h - 1.5, xo - s, hy, h + 1.5, {}, ["y0", "y1"]);
    // collision: one thick wall from the floor to above the ball's ceiling
    solid(xi, -hy - 60, 0, xi + s * 44, hy + 60, TOP);
  }
  // the net under the beams, and the ball's ceiling right on it
  flat("net_top", -hx, -hy, hx, hy, NET_TOP, 128, DOWN, { twoSided: true });
  solid(-hx - 44, -hy - 60, lid, hx + 44, hy + 60, TOP);

  for (const s of [1, -1]) {
    const team = s > 0 ? "red" : "blue", gy = s * hy, P = GOAL.post, back = gy + s * GOAL.depth, look = s > 0 ? NY : PY;
    const ya = Math.min(gy, back), yb = Math.max(gy, back);
    // frame in the team's colour (striped like every hall goal), centred on the goal line
    for (const e of [-1, 1]) box(`goal_${team}`, e * GOAL.half, gy - P / 2, 0, e * mouth, gy + P / 2, bar, { solid: true }, ["bottom"], 48);
    box(`goal_${team}`, -GOAL.half, gy - P / 2, GOAL.height, GOAL.half, gy + P / 2, bar, { solid: true }, ["x0", "x1"], 48);
    // the recess behind it: dark walls, a net hanging 3 units in front of them
    wallX("dark", -mouth, ya, yb, 0, bar, PX); wallX("dark", mouth, ya, yb, 0, bar, NX); wallY("dark", back, -mouth, mouth, 0, bar, look);
    flat("dark", -mouth, ya, mouth, yb, bar, 64, DOWN);
    const n = 3, net = { twoSided: true };
    wallX("goal_net", -mouth + n, ya, yb, 0, bar - n, PX, 40, net); wallX("goal_net", mouth - n, ya, yb, 0, bar - n, NX, 40, net);
    wallY("goal_net", back - s * n, -mouth + n, mouth - n, 0, bar - n, look, 40, net);
    flat("goal_net", -mouth + n, ya, mouth - n, yb, bar - n, 40, DOWN, net);
    // collision: the end wall beside and above the mouth, then the recess (sides, back, ceiling)
    solid(-HX - 40, gy, 0, -mouth, gy + s * 44, TOP); solid(mouth, gy, 0, HX + 40, gy + s * 44, TOP); solid(-mouth, gy, bar, mouth, gy + s * 44, TOP);
    solid(-mouth - 40, gy, 0, -mouth, back, bar + 40); solid(mouth, gy, 0, mouth + 40, back, bar + 40);
    solid(-mouth - 40, back, 0, mouth + 40, back + s * 40, bar + 40); solid(-mouth - 40, gy, bar, mouth + 40, back + s * 40, bar + 40);
    // the backboard over the goal is solid too: a ball off the board comes back into play
    solid(-64, gy - s * 16, 184, 64, gy, 264);
  }

  // =================================================================================================
  // the hall: end walls (impact panels, wall bars, backboard, scoreboard), side walls behind the
  // fans (timber, windows on the west, the mural on the east), ceiling with beams and LED panels
  // =================================================================================================
  G = "hall";
  for (const s of [1, -1]) {
    const team = s > 0 ? "red" : "blue", wy = s * hy, look = s > 0 ? NY : PY, PANEL = 150;
    const pic = s > 0 ? [0, 0, 1, 1] : [1, 0, 0, 1];   // pictures read left to right seen from the court
    // impact panels with the goal's opening, the team's stripe, white wall above
    // the impact panels beside the goal pick the painting's colours up where it meets the wall
    wallY(s > 0 ? "panel_mint" : "panel_coral", wy, -HX, -mouth, 0, PANEL, look); wallY(s > 0 ? "panel_mustard" : "panel_navy", wy, mouth, HX, 0, PANEL, look); wallY("wall_panel", wy, -mouth, mouth, bar, PANEL, look);
    wallY(`stripe_${team}`, wy - s * 0.5, -HX, HX, PANEL - 14, PANEL, look, [0, 0, 1, 1]);
    wallY("wall_white", wy, -HX, HX, PANEL, CEIL, look);
    // wall bars left and right of the goal
    for (const e of [-1, 1]) wallY("wall_bars", wy - s * 3, e > 0 ? 250 : -470, e > 0 ? 470 : -250, 4, 132, look, [0, 0, 2, 1], { twoSided: true });
    // basketball backboard with its ring, on a bracket
    const by = wy - s * 16;
    wallY("backboard", by, -62, 62, 186, 262, look, pic, { twoSided: true });
    // bracket behind the board: it starts a unit behind the board and has no face towards it (a face in the
    // board's plane flickered through the board, owner 2026-10-02)
    box("steel_dark", -5, by + s * 1, 214, 5, wy, 234, {}, [s > 0 ? "y0" : "y1"]);
    const cy = by - s * 21, R = 17, seg = 14;
    for (let k = 0; k < seg; k++) {
      const p = (r, t, h) => [Math.cos(t) * r, cy + Math.sin(t) * r, Z(h)], t0 = k * 2 * Math.PI / seg, t1 = (k + 1) * 2 * Math.PI / seg;
      scene.quad(M("ring"), p(R + 1.6, t0, 204), p(R + 1.6, t1, 204), p(R - 1.6, t1, 204), p(R - 1.6, t0, 204), [[0, 0], [1, 0], [1, 1], [0, 1]], UP, O({ twoSided: true }));
      // the net under the ring, narrowing downwards
      scene.quad(M("goal_net"), p(R, t0, 204), p(R, t1, 204), p(R * 0.62, t1, 178), p(R * 0.62, t0, 178), [[k / 2, 0], [(k + 1) / 2, 0], [(k + 1) / 2, 0.65], [k / 2, 0.65]], null, O({ twoSided: true }));
    }
    box("ring", -3, by - s * 0.2, 202, 3, by - s * 5, 206, {}, [s > 0 ? "y1" : "y0"]);
    // scoreboard (red end), the hall's sign (blue end), both lit; the team's banner on the other side
    wallY(s > 0 ? "scoreboard" : "sign", wy - s * 2, s > 0 ? 220 : -480, s > 0 ? 480 : -220, 186, 276, look, pic);
    box("steel_dark", s > 0 ? 214 : -486, wy - s * 1.9, 180, s > 0 ? 486 : -214, wy, 282, {}, [s > 0 ? "y1" : "y0"]);
    wallY(`banner_${team}`, wy - s * 1.5, s > 0 ? -470 : 250, s > 0 ? -250 : 470, 168, 288, look, pic);
  }
  for (const s of [1, -1]) {
    const wx = s * HX, look = s > 0 ? NX : PX;
    if (s < 0) wallX("wall_timber", wx, -hy, hy, 0, 150, look, [-hy / 256, 0, hy / 256, 150 / 256]);
    if (s < 0) {
      // west: a band of windows under the ceiling
      wallX("wall_white", wx, -hy, hy, 150, 176, look); wallX("windows", wx, -hy, hy, 176, 292, look, [0, 0, (2 * hy) / 240, 1]); wallX("wall_white", wx, -hy, hy, 292, CEIL, look);
    } else {
      // east: glass from the floor to the roof, steel posts every bay, a rail at head height
      wallX("glass", wx, -hy, hy, 6, CEIL - 6, look, [0, 0, (2 * hy) / GLASS.bay, 1], { twoSided: true });
      box("steel_dark", wx - 4, -hy, 0, wx + 4, hy, 6, {}, ["bottom", "y0", "y1"]); box("steel_dark", wx - 4, -hy, CEIL - 6, wx + 4, hy, CEIL, {}, ["top", "y0", "y1"]);
      box("steel_dark", wx - 2.5, -hy, GLASS.transom - 2, wx + 2.5, hy, GLASS.transom + 2, {}, ["y0", "y1"]);
      for (let y = -hy; y <= hy + 1; y += GLASS.bay) box("steel_dark", wx - 3.5, Math.max(-hy, y - 3), 6, wx + 3.5, Math.min(hy, y + 3), CEIL - 6, {}, ["bottom", "top"]);
    }
    // the fans' steps
    for (let k = 0; k < STAND.rows; k++) {
      const r = standRow(k), xa = s * r.x0, xb = s * r.x1;
      flat("lino", Math.min(xa, xb), -hy, Math.max(xa, xb), hy, r.h, 128);
      wallX("riser", xa, -hy, hy, k ? r.h - STAND.rise : 0, r.h, look, [-hy / 64, 0, hy / 64, 1]);
      crowd.push({ a: [s * (r.x0 + STAND.depth * 0.5), -hy + 14], b: [s * (r.x0 + STAND.depth * 0.5), hy - 14], h: r.h, look: [-s, 0], group: 2, name: s > 0 ? "east" : "west" });
    }
    // warm wall lamps over the fans
    for (const ly of [-480, 0, 480]) lights.push({ at: [s * (hx + 78), ly, Z(200)], brightness: 0.3, range: 520, color: "255 228 196" });
  }
  // ceiling, glulam beams across the hall, LED panels between them
  flat("ceiling", -HX, -hy, GLASS.roofFrom, hy, CEIL, 128, DOWN);
  flat("glass", GLASS.roofFrom, -hy, HX, hy, CEIL, GLASS.bay, DOWN, { twoSided: true });
  box("steel_dark", GLASS.roofFrom - 3, -hy, CEIL - 8, GLASS.roofFrom + 3, hy, CEIL, {}, ["top", "y0", "y1"]);
  for (let y = -hy + HALL.beamEvery / 2; y < hy; y += HALL.beamEvery) box("beam", -HX, y - 8, CEIL - HALL.beamH, HX, y + 8, CEIL, {}, ["top", "x0", "x1"], 128);
  for (const ly of LAMPS.ys) for (const lx of LAMPS.xs) {
    flat("led_panel", lx - LAMPS.w / 2, ly - LAMPS.d / 2, lx + LAMPS.w / 2, ly + LAMPS.d / 2, CEIL - 1.5, LAMPS.w, DOWN);
    box("steel_dark", lx - LAMPS.w / 2 - 2, ly - LAMPS.d / 2 - 2, CEIL - 3, lx + LAMPS.w / 2 + 2, ly + LAMPS.d / 2 + 2, CEIL, {}, ["top", "bottom"]);
    lights.push({ at: [lx, ly, Z(CEIL - 30)], brightness: 0.6, range: 1200 });   // 0.9 was too bright (owner 2026-10-02)
  }
  // =================================================================================================
  // outside the glass: a roof terrace (gravel, a parapet, cooling units, an aerial, a plant room at each
  // end), and the city in the evening as two painted layers - near roofs, far towers with lit windows
  // =================================================================================================
  G = "outside";
  {
    const T = TERRACE;
    flat("roof_gravel", HX, -hy - 60, T.x1, hy + 60, -1, 128);
    box("parapet", T.x1 - 16, -hy - 60, -1, T.x1, hy + 60, T.parapet, {}, ["bottom", "y0", "y1"], 96);
    box("steel_dark", T.x1 - 18, -hy - 60, T.parapet, T.x1 + 2, hy + 60, T.parapet + 3, {}, ["bottom", "y0", "y1"]);
    for (const s of [1, -1]) {
      const y0 = s * T.room.y, y1 = s * (hy + 60);
      box("plant_wall", HX + 6, Math.min(y0, y1), -1, T.room.x1, Math.max(y0, y1), T.room.h, {}, ["bottom", "top", "x0", s > 0 ? "y1" : "y0"], 128);
      wallY("plant_door", y0 - s * 0.6, s > 0 ? 780 : 720, s > 0 ? 720 : 780, 0, 86, s > 0 ? NY : PY, [0, 0, 1, 1]);
      lights.push({ at: [750, y0 - s * 40, Z(120)], brightness: 0.25, range: 300, color: "255 190 130" });
    }
    // cooling units, pipes, an aerial
    for (const [x, y, w, d, h] of [[760, -300, 56, 40, 44], [760, -230, 56, 40, 44], [800, 180, 70, 50, 52], [730, 420, 44, 44, 36]]) { box("ac_unit", x - w / 2, y - d / 2, 4, x + w / 2, y + d / 2, h, {}, ["bottom"], 48); for (const e of [-1, 1]) box("steel_dark", x + e * (w / 2 - 4) - 2, y - d / 2, -1, x + e * (w / 2 - 4) + 2, y + d / 2, 4, {}, ["bottom", "top"]); }
    box("steel_dark", 838, 40, -1, 842, 44, 250, {}, ["bottom"]); for (const h of [150, 190, 230]) box("steel_dark", 820, 41, h, 860, 43, h + 2, {}, []);
    box("steel_dark", 690, -520, 8, 880, -514, 14, {}, []); box("steel_dark", 690, -500, 8, 880, -494, 14, {}, []);
    // the city: the near layer ends below the eye, the far one rises above it
    wallX("city_near", CITY.near, -CITY.y, CITY.y, CITY.bottom, CITY.nearTop, NX, [1, 0, 0, 1]);
    wallX("city_far", CITY.far, -CITY.y, CITY.y, CITY.bottom, CITY.top, NX, [1, 0, 0, 1]);
    // the last light of the day falls in from the east side
    for (const y of [-540, -180, 180, 540]) lights.push({ at: [HX - 60, y, Z(150)], brightness: 0.5, range: 760, color: "255 170 110" });
  }
  return { scene, crowd, lights };
}
export { FLOOR };
