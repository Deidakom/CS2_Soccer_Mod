// SoccerMod gym: every face of the map, grouped by material and by model (opts.group).
// layout.mjs holds the numbers. Collision is a handful of plain thick boxes (physOnly: in the
// collision mesh only), so the ball and the players meet simple, solid shapes.
import { FLOOR, Z, COURT, BOARD, MESH, NET_TOP, GOAL, HALL, STAND, standRow, LAMPS, courtLines, AREA } from "./layout.mjs";
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
    for (const s of [1, -1]) fan(s > 0 ? "paint_red" : "paint_blue", 0, s * AREA.y, AREA.r, s > 0 ? Math.PI : 0, s > 0 ? 2 * Math.PI : Math.PI, 28, 0.12, ([x, y]) => [x / 64, -y / 64]);
    fan("emblem", 0, 0, AREA.emblem, 0, 2 * Math.PI, 48, 0.12, ([x, y]) => [0.5 + x / (2 * AREA.emblem), 0.5 - y / (2 * AREA.emblem)]);
    const lines = courtLines();
    for (const [colour, h] of [["yellow", 0.25], ["black", 0.5], ["white", 0.75]])
      for (const poly of lines[colour]) scene.poly(M(`line_${colour}`), poly.map(([x, y]) => [x, y, Z(h)]), poly.map(([x, y]) => [x / 32, -y / 32]), UP, O());
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
    wallY("wall_panel", wy, -HX, -mouth, 0, PANEL, look); wallY("wall_panel", wy, mouth, HX, 0, PANEL, look); wallY("wall_panel", wy, -mouth, mouth, bar, PANEL, look);
    wallY(`stripe_${team}`, wy - s * 0.5, -HX, HX, PANEL - 14, PANEL, look, [0, 0, 1, 1]);
    wallY("wall_white", wy, -HX, HX, PANEL, CEIL, look);
    // wall bars left and right of the goal
    for (const e of [-1, 1]) wallY("wall_bars", wy - s * 3, e > 0 ? 250 : -470, e > 0 ? 470 : -250, 4, 132, look, [0, 0, 2, 1], { twoSided: true });
    // basketball backboard with its ring, on a bracket
    const by = wy - s * 16;
    wallY("backboard", by, -62, 62, 186, 262, look, pic, { twoSided: true });
    box("steel_dark", -5, by, 214, 5, wy, 234);
    const cy = by - s * 21, R = 17, seg = 14;
    for (let k = 0; k < seg; k++) {
      const p = (r, t, h) => [Math.cos(t) * r, cy + Math.sin(t) * r, Z(h)], t0 = k * 2 * Math.PI / seg, t1 = (k + 1) * 2 * Math.PI / seg;
      scene.quad(M("ring"), p(R + 1.6, t0, 204), p(R + 1.6, t1, 204), p(R - 1.6, t1, 204), p(R - 1.6, t0, 204), [[0, 0], [1, 0], [1, 1], [0, 1]], UP, O({ twoSided: true }));
      // the net under the ring, narrowing downwards
      scene.quad(M("goal_net"), p(R, t0, 204), p(R, t1, 204), p(R * 0.62, t1, 178), p(R * 0.62, t0, 178), [[k / 2, 0], [(k + 1) / 2, 0], [(k + 1) / 2, 0.65], [k / 2, 0.65]], null, O({ twoSided: true }));
    }
    box("ring", -3, by, 202, 3, by - s * 5, 206);
    // scoreboard (red end), the hall's sign (blue end), both lit; the team's banner on the other side
    wallY(s > 0 ? "scoreboard" : "sign", wy - s * 2, s > 0 ? 220 : -480, s > 0 ? 480 : -220, 186, 276, look, pic);
    box("steel_dark", s > 0 ? 214 : -486, wy - s * 1.9, 180, s > 0 ? 486 : -214, wy, 282, {}, [s > 0 ? "y1" : "y0"]);
    wallY(`banner_${team}`, wy - s * 1.5, s > 0 ? -470 : 250, s > 0 ? -250 : 470, 168, 288, look, pic);
  }
  for (const s of [1, -1]) {
    const wx = s * HX, look = s > 0 ? NX : PX;
    wallX("wall_timber", wx, -hy, hy, 0, 150, look, [-hy / 256, 0, hy / 256, 150 / 256]);
    if (s < 0) {
      // west: a band of windows under the ceiling
      wallX("wall_white", wx, -hy, hy, 150, 176, look); wallX("windows", wx, -hy, hy, 176, 292, look, [0, 0, (2 * hy) / 240, 1]); wallX("wall_white", wx, -hy, hy, 292, CEIL, look);
    } else {
      // east: the hall's mural
      wallX("wall_white", wx, -hy, hy, 150, CEIL, look);
      wallX("mural", wx - 1.5, -360, 360, 160, 300, look, [1, 0, 0, 1]);
    }
    // the fans' steps
    for (let k = 0; k < STAND.rows; k++) {
      const r = standRow(k), xa = s * r.x0, xb = s * r.x1;
      flat("lino", Math.min(xa, xb), -hy, Math.max(xa, xb), hy, r.h, 128);
      wallX("riser", xa, -hy, hy, k ? r.h - STAND.rise : 0, r.h, look, [-hy / 64, 0, hy / 64, 1]);
      crowd.push({ a: [s * (r.x0 + STAND.depth * 0.5), -hy + 14], b: [s * (r.x0 + STAND.depth * 0.5), hy - 14], h: r.h, look: [-s, 0], group: 2, name: s > 0 ? "east" : "west" });
    }
    // warm wall lamps over the fans
    for (const ly of [-480, 0, 480]) lights.push({ at: [s * (hx + 78), ly, Z(200)], brightness: 0.45, range: 520, color: "255 228 196" });
  }
  // ceiling, glulam beams across the hall, LED panels between them
  flat("ceiling", -HX, -hy, HX, hy, CEIL, 128, DOWN);
  for (let y = -hy + HALL.beamEvery / 2; y < hy; y += HALL.beamEvery) box("beam", -HX, y - 8, CEIL - HALL.beamH, HX, y + 8, CEIL, {}, ["top", "x0", "x1"], 128);
  for (const ly of LAMPS.ys) for (const lx of LAMPS.xs) {
    flat("led_panel", lx - LAMPS.w / 2, ly - LAMPS.d / 2, lx + LAMPS.w / 2, ly + LAMPS.d / 2, CEIL - 1.5, LAMPS.w, DOWN);
    box("steel_dark", lx - LAMPS.w / 2 - 2, ly - LAMPS.d / 2 - 2, CEIL - 3, lx + LAMPS.w / 2 + 2, ly + LAMPS.d / 2 + 2, CEIL, {}, ["top", "bottom"]);
    lights.push({ at: [lx, ly, Z(CEIL - 30)], brightness: 0.9, range: 1200 });
  }
  return { scene, crowd, lights };
}
export { FLOOR };
