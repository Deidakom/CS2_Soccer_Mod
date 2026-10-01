// SoccerMod indoor hall (ka_soccermod_indoor): every number of the map in one place.
// Owner 2026-10-01: "a soccer indoor hall, about three to four people per team, like you would
// play indoor football, state of the art, many details, fans included, a really sharp pitch".
//
// Units as in CS2 (a player is 72 tall, 40 units are about a metre). The pitch centre is the
// origin, +y is the red end, -y the blue end, -x the main stand. FLOOR is the turf (v8's floor
// height, so the plugin's pitch-local numbers are the world numbers). Heights named "h" are
// above the floor.
export const MAP_NAME = "ka_soccermod_indoor";
export const FLOOR = -32;
export const Z = (h) => FLOOR + h;

// The court: boards all the way round with rounded corners, the ball never leaves it.
// hx / gy are the inner faces of the boards; the goal line is the end boards' inner face.
export const PITCH = { hx: 840, gy: 1150, corner: 190 };
export const BOARD = { h: 44, t: 8, capH: 4 };
export const GLASS = { h0: BOARD.h + BOARD.capH, h1: 176, d: 3 };            // acrylic panels on the boards
export const NET = { h1: 492, d: 4, postEvery: 168 };                         // side nets up to the ceiling net
// v8's goal mouth (the plugin's goal frame, moving net and net pocket are made for it): 248 x 97
// between the posts and under the crossbar, posts 8 square centred on the goal line. The goal is
// set into the end boards; behind the mouth a housing with room for the net and its pocket.
export const GOAL = { half: 124, post: 8, height: 97, housingHalf: 204, housingDepth: 216, housingH: 146, wall: 10 };

// The hall round the court.
export const HALL = { x0: -1400, x1: 1290, y: 1720, eave: 540, ridge: 790 };
export const WALK = { w: 100 };                                               // walkway behind the boards (west / east)
// stands: tread k (0 = front row) is `depth` deep and `rise` higher than the one before
export const WEST = { x: -(PITCH.hx + BOARD.t + WALK.w), rows: 8, depth: 34, rise: 17, h0: 34, y: 1010, aisles: [-505, 0, 505], aisleW: 56 };
export const EAST = { x: PITCH.hx + BOARD.t + WALK.w, rows: 5, depth: 34, rise: 17, h0: 34, y: 1010, aisles: [-505, 0, 505], aisleW: 56 };
export const END = { y: PITCH.gy + BOARD.t + 236, steps: 5, depth: 46, rise: 15, h0: 30, x: 770 };   // standing terraces behind the goals
export const LOUNGE = { h0: 322, h1: 470, depth: 196 };                        // glass-fronted lounge above the west concourse
export const SEAT = { width: 22 };

export const westRow = (k) => ({ h: WEST.h0 + WEST.rise * k, x0: WEST.x - WEST.depth * k, x1: WEST.x - WEST.depth * (k + 1) });
export const eastRow = (k) => ({ h: EAST.h0 + EAST.rise * k, x0: EAST.x + EAST.depth * k, x1: EAST.x + EAST.depth * (k + 1) });
export const endStep = (k) => ({ h: END.h0 + END.rise * k, y0: END.y + END.depth * k, y1: END.y + END.depth * (k + 1) });
export const WEST_TOP = { x: WEST.x - WEST.depth * WEST.rows, h: WEST.h0 + WEST.rise * WEST.rows };
export const EAST_TOP = { x: EAST.x + EAST.depth * EAST.rows, h: EAST.h0 + EAST.rise * EAST.rows };
export const END_TOP = { y: END.y + END.depth * END.steps, h: END.h0 + END.rise * END.steps };

// The barrel roof spans x; trusses every TRUSS.step along y.
export const ROOF_SEGMENTS = 16;
export const roofH = (x) => { const t = (x - HALL.x0) / (HALL.x1 - HALL.x0); return HALL.eave + (HALL.ridge - HALL.eave) * Math.sin(Math.PI * t); };
export const TRUSS = { step: 215, depth: 70, y: [] };
for (let y = -HALL.y + 107.5; y < HALL.y; y += TRUSS.step) TRUSS.y.push(y);
// LED light bars above the ceiling net: 4 across, one row per truss bay over the court
export const LIGHTS = { h: 512, xs: [-630, -210, 210, 630], ys: [] };
for (let y = -1075; y <= 1075; y += 215) LIGHTS.ys.push(y);
export const CUBE = { half: 120, h0: 506, h1: 606 };                           // video cube over the centre spot

// ---- the board line: a rounded rectangle, counter-clockwise from the middle of the east side ----
// Offset d moves it outwards (d = BOARD.t is the boards' outer face). Each point knows whether the
// segment that starts there lies in a goal span (|x| < GOAL.housingHalf on an end straight).
export function boardLine(d = 0) {
  const { hx, gy, corner } = PITCH, r = corner + d, X = hx + d, Y = gy + d, cx = hx - corner, cy = gy - corner;
  const pts = [], G = GOAL.housingHalf, ARC = 10;
  const push = (x, y, post = true) => pts.push({ x, y, post });
  const straight = (x0, y0, x1, y1, step, cuts = []) => {
    const len = Math.hypot(x1 - x0, y1 - y0), n = Math.max(1, Math.round(len / step));
    const ts = new Set([...Array(n).keys()].map((k) => k / n));
    for (const c of cuts) ts.add(c);
    for (const t of [...ts].sort((a, b) => a - b)) push(x0 + (x1 - x0) * t, y0 + (y1 - y0) * t);
  };
  const arc = (ax, ay, a0) => { for (let k = 0; k < ARC; k++) { const a = a0 + (Math.PI / 2) * (k / ARC); push(ax + Math.cos(a) * r, ay + Math.sin(a) * r, k % 5 === 0); } };
  const endCuts = [(cx - G) / (2 * cx), (cx + G) / (2 * cx)];
  straight(X, 0, X, cy, 168);                       // east side, north half
  arc(cx, cy, 0);                                   // north-east corner
  straight(cx, Y, -cx, Y, 168, endCuts);            // north end (red), east to west
  arc(-cx, cy, Math.PI / 2);
  straight(-X, cy, -X, -cy, 168);                   // west side
  arc(-cx, -cy, Math.PI);
  straight(-cx, -Y, cx, -Y, 168, endCuts);          // south end (blue), west to east
  arc(cx, -cy, Math.PI * 1.5);
  straight(X, -cy, X, 0, 168);
  // lengths, normals and the goal flag
  let S = 0;
  for (let i = 0; i < pts.length; i++) {
    const a = pts[i], b = pts[(i + 1) % pts.length], l = Math.hypot(b.x - a.x, b.y - a.y);
    a.S = S; S += l; a.len = l; a.nx = (b.y - a.y) / l; a.ny = -(b.x - a.x) / l;   // outward normal of the segment
    a.goal = Math.abs(Math.abs(a.y) - Y) < 1e-6 && Math.abs(Math.abs(b.y) - Y) < 1e-6 && Math.abs(a.x) <= G + 1e-6 && Math.abs(b.x) <= G + 1e-6;
  }
  return { pts, length: S };
}
