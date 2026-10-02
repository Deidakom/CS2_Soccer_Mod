// SoccerMod gym (soccer_2v2_arena): every number of the map in one place.
// Owner 2026-10-02: "a new map only for two against two, much smaller, really a hall, a very small
// hall with a wooden floor, a typical indoor hall, maybe a bit of a cage - a kind of street football".
//
// A school sports hall with a parquet floor. The end walls are in play (the goals are set into
// them), the long sides are a cage: low kick boards, steel mesh, a net up to the beams, with the
// fans standing right behind it. Units as in CS2 (a player is 72 tall). Centre spot = origin,
// +y = the red end, FLOOR = the parquet (v8's floor height, so the plugin's pitch-local numbers are
// the world numbers). Heights named "h" are above the floor.
export const MAP_NAME = "soccer_2v2_arena";
export const FLOOR = -32;
export const Z = (h) => FLOOR + h;

export const COURT = { hx: 520, hy: 720, lid: 284 };                 // inner faces of the cage / the end walls; ball ceiling
export const BOARD = { h: 40, t: 6 };                                // timber kick boards under the mesh
export const MESH = { h1: 210, bay: 120 };                           // steel mesh panels on the boards
export const NET_TOP = 284;
// the 2v2 goal of soccer_multi_indoor, set into the end wall: 156 x 62 between the posts and under the bar
export const GOAL = { half: 78, post: 6, height: 62, depth: 72 };
export const HALL = { x: 662, y: COURT.hy, ceiling: 310, beamH: 26, beamEvery: 180 };
// the fans stand on three steps right behind the cage, so every row looks over the kick boards
export const STAND = { x0: COURT.hx + BOARD.t + 4, depth: 44, rise: 20, first: 16, rows: 3 };
export const standRow = (k) => ({ x0: STAND.x0 + k * STAND.depth, x1: STAND.x0 + (k + 1) * STAND.depth, h: STAND.first + k * STAND.rise });
export const LAMPS = { xs: [-330, 0, 330], ys: [-540, -360, -180, 0, 180, 360, 540], w: 130, d: 44 };   // between the beams

// the painted lines as convex polygons [x, y], by colour: white = the game, the others are the hall's
// basketball (black) and volleyball (yellow) markings, as every school hall has them
export function courtLines() {
  const out = { white: [], black: [], yellow: [] };
  const rect = (c, x0, y0, x1, y1) => out[c].push([[Math.min(x0, x1), Math.min(y0, y1)], [Math.max(x0, x1), Math.min(y0, y1)], [Math.max(x0, x1), Math.max(y0, y1)], [Math.min(x0, x1), Math.max(y0, y1)]]);
  const ring = (c, cx, cy, r, a0, a1, segs, w = 5) => {
    for (let k = 0; k < segs; k++) {
      const t0 = a0 + (a1 - a0) * k / segs, t1 = a0 + (a1 - a0) * (k + 1) / segs, p = (rr, t) => [cx + Math.cos(t) * rr, cy + Math.sin(t) * rr];
      out[c].push([p(r - w / 2, t0), p(r + w / 2, t0), p(r + w / 2, t1), p(r - w / 2, t1)]);
    }
  };
  const disc = (c, cx, cy, r) => out[c].push([...Array(12).keys()].map((k) => [cx + Math.cos(k * Math.PI / 6) * r, cy + Math.sin(k * Math.PI / 6) * r]));
  const W = 5, bx = COURT.hx - 14, by = COURT.hy - 14;
  // the game: boundary, halfway line, centre circle, the areas in front of the goals, penalty spots
  rect("white", -bx, -by, bx, -by + W); rect("white", -bx, by - W, bx, by); rect("white", -bx, -by, -bx + W, by); rect("white", bx - W, -by, bx, by);
  rect("white", -bx, -W / 2, bx, W / 2); ring("white", 0, 0, 110, 0, Math.PI * 2, 48); disc("white", 0, 0, 6);
  for (const s of [1, -1]) {
    ring("white", 0, s * by, 190, s > 0 ? Math.PI : 0, s > 0 ? Math.PI * 2 : Math.PI, 28);
    disc("white", 0, s * (by - 250), 5);
    // basketball: the key and the three-point arc
    rect("black", -72, s * by, -68, s * (by - 230)); rect("black", 68, s * by, 72, s * (by - 230)); rect("black", -72, s * (by - 230), 72, s * (by - 226));
    ring("black", 0, s * (by - 228), 70, s > 0 ? Math.PI : 0, s > 0 ? Math.PI * 2 : Math.PI, 14, 4);
    ring("black", 0, s * (by - 60), 330, s > 0 ? Math.PI : 0, s > 0 ? Math.PI * 2 : Math.PI, 32, 4);
    // volleyball: attack line
    rect("yellow", -360, s * 118, 360, s * 122);
  }
  // volleyball court
  rect("yellow", -360, -362, 360, -358); rect("yellow", -360, 358, 360, 362); rect("yellow", -362, -362, -358, 362); rect("yellow", 358, -362, 362, 362);
  return out;
}
// painted areas: the half circle in front of each goal in the team's colour, the emblem in the centre circle
export const AREA = { r: 187.5, y: COURT.hy - 14, emblem: 107.5 };
