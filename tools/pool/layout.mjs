// SoccerMod 1v1 cage (soccer_1v1_cage): every number of the map in one place.
// Owner 2026-10-02: "a 1v1 map that is built in a cage, it can also be a mix of underground and indoor
// scenario ... well designed outstanding idea, jaw-dropping details and just fun to play". His pick: a
// drained swimming pool - "an old indoor bathhouse: the pitch lies on the bottom of the emptied pool, tiled
// walls are in play, a steel cage stands on the pool edge; glowing underwater lamps in the walls, depth
// markings, lane lines, diving tower and gallery full of onlookers, light through a glass roof".
//
// Units as in CS2 (a player is 72 tall, 40 units are about a metre). Centre spot = origin = the pool's
// main drain, +y = the home end (the deep end with the diving tower), -y = the away end (the starting
// blocks). FLOOR = the pool's bottom (v8's floor height, so the plugin's pitch-local numbers are the
// world numbers). "h" = height above the pool's bottom.
export const MAP_NAME = "soccer_1v1_cage";
export const FLOOR = -32;
export const Z = (h) => FLOOR + h;

// The pitch is the pool's bottom: 600 x 880. The tiled walls are in play up to the pool's edge.
export const POOL = { hx: 300, hy: 440, depth: 120 };
// The cage stands on the edge: steel posts, mesh up to the lid, a mesh lid on top (the ball's ceiling).
export const CAGE = { top: 300, bay: 110 };
// the 2v2 goal of soccer_multi_indoor (156 x 62) set into the pool's end walls like an overflow niche
export const GOAL = { half: 78, post: 6, height: 62, depth: 72 };
// Round the pool: the deck at the edge's height, an arcade of columns at its outer side, behind the arcade the
// changing cabins, above them the gallery; the hall's walls and its vaulted glass roof.
export const DECK = { h: POOL.depth, w: 150 };
export const ARCADE = { x: POOL.hx + DECK.w, bay: 176, col: 26, spring: 250, top: 286 };       // column line, arch heights
export const GALLERY = { h: 300, depth: 120, rail: 38 };
export const HALL = { x: POOL.hx + DECK.w + GALLERY.depth, y: POOL.hy + DECK.w + 130, eave: 520,
  roof: [[0, 650], [190, 636], [380, 596], [570, 520]] };                                         // half profile of the vault: [x, h]
export const SKYLIGHT = 380;                                                                    // the vault is glass up to this |x|
// underwater lamps in the pool's walls (round, glowing): long walls [y], end walls [x]; all at this height
export const LAMPS = { h: 58, r: 13, ys: [-365, -219, -73, 73, 219, 365], xs: [-200, 200] };
// the diving tower at the deep end: a concrete mast on the deck, platforms towards the pool [h above the deck, length]
export const TOWER = { y: 640, mast: 44, platforms: [{ h: 150, len: 170, w: 84 }, { h: 262, len: 150, w: 76 }], board: { h: 44, len: 150, x: -150 } };
export const BLOCKS = { y: -(POOL.hy + 34), xs: [-240, -120, 0, 120, 240] };                    // starting blocks at the away end
export const WORKLIGHTS = [[-385, -540], [385, -540], [-385, 540], [385, 540]];                 // tripod lamps on the deck's corners

// the pool's lane lines (black tile strips with a T at both ends) and the painted pitch lines
export function lanes() {
  const out = [], w = 10, t = 46, y1 = POOL.hy - 70;
  for (const x of [-200, -100, 0, 100, 200]) { out.push([[x - w / 2, -y1], [x + w / 2, -y1], [x + w / 2, y1], [x - w / 2, y1]]); for (const s of [1, -1]) out.push([[x - t / 2, s * y1 - w / 2], [x + t / 2, s * y1 - w / 2], [x + t / 2, s * y1 + w / 2], [x - t / 2, s * y1 + w / 2]]); }
  return out;
}
export function pitchLines() {
  const out = [], W = 5, bx = POOL.hx - 10, by = POOL.hy - 10;
  const rect = (x0, y0, x1, y1) => out.push([[Math.min(x0, x1), Math.min(y0, y1)], [Math.max(x0, x1), Math.min(y0, y1)], [Math.max(x0, x1), Math.max(y0, y1)], [Math.min(x0, x1), Math.max(y0, y1)]]);
  const ring = (cx, cy, r, a0, a1, segs) => { for (let k = 0; k < segs; k++) { const t0 = a0 + (a1 - a0) * k / segs, t1 = a0 + (a1 - a0) * (k + 1) / segs, p = (rr, t) => [cx + Math.cos(t) * rr, cy + Math.sin(t) * rr]; out.push([p(r - W / 2, t0), p(r + W / 2, t0), p(r + W / 2, t1), p(r - W / 2, t1)]); } };
  rect(-bx, -by, bx, -by + W); rect(-bx, by - W, bx, by); rect(-bx, -by, -bx + W, by); rect(bx - W, -by, bx, by);
  rect(-bx, -W / 2, bx, W / 2); ring(0, 0, 84, 0, Math.PI * 2, 44);
  for (const s of [1, -1]) ring(0, s * by, 150, s > 0 ? Math.PI : 0, s > 0 ? Math.PI * 2 : Math.PI, 26);
  return out;
}
