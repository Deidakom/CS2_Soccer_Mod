// SoccerMod street arena (soccer_street_arena): every number of the map in one place.
// Owner 2026-10-02: "a new map based on street soccer, inside the city, a bit of the flair of Brazil,
// a bit of New York ... no LED banners, graffiti on the walls, maybe on the ground as well, really
// similar to FIFA Street ... as detailed as possible, not generic". His answers: a court under an
// (After his first look: "keep the scenario to Brazil, remove the skyscrapers, add a Brazil flair behind the
// other goal as well" - so the line is a Brazilian metro viaduct now, the north end a colonial street with
// a church, and the court is one big painting of rays under a canopy of festival flags.)
// elevated subway line with Brazilian colour round it, 3 against 3, golden hour. Later: onlookers
// in street clothes (no team colours), the fitting features of the other maps, no fireworks.
//
// Units as in CS2 (a player is 72 tall, 40 units are about a metre). Centre spot = origin,
// +y = the red end (the New York end), -y = the blue end (the hillside houses), -x = the street of
// shops (the sun is low in the west), +x = the elevated line. FLOOR = the asphalt (v8's floor
// height, so the plugin's pitch-local numbers are the world numbers). "h" = height above the floor.
export const MAP_NAME = "soccer_street_arena";
export const FLOOR = -32;
export const Z = (h) => FLOOR + h;

// The court: inner faces of the walls. Walls and fences are in play, a net closes it on top.
// lid = the fences' top. Nothing closes the court on top any more (owner 2026-10-02: "for the ceiling I think
// we can remove it, just make sure the ball can't get out"): invisible walls go on up to `ceil`, where an
// invisible lid lies.
export const COURT = { hx: 650, hy: 900, lid: 360, ceil: 1000 };
export const WEST_WALL = { h: 46, t: 12 };            // low painted wall, chain-link fence on it
export const EAST_WALL = { h: 150, t: 16 };           // retaining wall: the street under the tracks lies 150 higher
export const END_WALL = { h: 120, t: 14 };            // end walls with the container goals set into them
export const FENCE = { top: COURT.lid, bay: 150 };
// v8's goal mouth (the plugin's goal frame, moving net and net pocket are made for it): 248 x 97,
// posts 8 square centred on the goal line. The housing behind it is a cut-open shipping container.
export const GOAL = { half: 124, post: 8, height: 97, housingHalf: 204, housingDepth: 216, housingH: 146, wall: 8 };

// West: sidewalk (wave mosaic), street, far sidewalk, a row of low shops.
export const WEST = { walk0: -(COURT.hx + WEST_WALL.t), walk1: -830, street1: -1090, far1: -1170, back: -1420, y: 1240 };
// East: the raised street under the elevated line, then tall tenements.
export const EAST = { h: EAST_WALL.h, x0: COURT.hx + EAST_WALL.t, facade: 1190, back: 1420, y: 1240 };
export const EL = {                                   // the elevated subway line, along y
  // it reaches 50 over the court's east edge, high above the net: the court lies "under the tracks"
  colX: [690, 950], bentEvery: 300, bentY0: -1200, bents: 9,
  girderH0: 430, girderH1: 500, deckH: 500, width: 400, cx: 800, railH: 536, y: 1500,
  portal: { x0: 560, x1: 1040, depth: 60, top: 740 },     // where the line disappears: under a building (north), into the hill (south)
  trackX: 700,                                              // the track the train runs on (the one over the court's edge)
};
// North (red end): yard behind the wall, then two tenements.
export const NORTH = { yard: COURT.hy + END_WALL.t, facade: 1210, back: 1500 };
// South (blue end): yard, then houses on three terraces up the hill, a tiled stairway in the middle.
export const SOUTH = { yard: -(COURT.hy + END_WALL.t), tiers: [{ y: -1190, h: 0 }, { y: -1400, h: 150 }, { y: -1600, h: 300 }], back: -1820, stairHalf: 70 };
export const BOUNDS = { x0: -1420, x1: 1420, y0: -1820, y1: 1560, top: 1200 };
// What stands behind the houses, as painted cut-outs inside the sky box: the hill with its houses and the
// statue (south), towers (north), rooftops against the sunset (west). The west one stays low: the sun is there.
export const FAR = { x: 1700, south: -2080, north: 2100, hillTop: 1400, bayTop: 1400, roofsTop: 700 };
// The court's painting: rays of colour from the centre spot (the materials, in the order they go round).
export const RAYS = { count: 28, start: 0.11, mats: ["paint_yellow", "paint_orange", "paint_coral", "paint_pink", "paint_blue", "paint_teal", "paint_green"] };
export const rayOf = (x, y) => Math.floor(((((Math.atan2(y, x) - RAYS.start) % (2 * Math.PI)) + 2 * Math.PI) % (2 * Math.PI)) / (2 * Math.PI / RAYS.count));
// The canopy of festival flags (a moving model, generate-street-canopy.mjs): strings from the lamp posts'
// line to the viaduct, [y at the west end, y at the east end]; and the kites over the roofs [x, y, h, roof point].
export const CANOPY = {
  strings: [[-600, -600], [-360, -360], [-120, -120], [120, 120], [360, 360], [600, 600], [-720, -480], [-480, -720], [-240, 0], [0, -240], [240, 480], [480, 240]],
  sag: 34, flagEvery: 15.5, flagW: 11, flagH: 16,
  kites: [{ at: [-520, -1480, 900], from: [-560, -1330, 250] }, { at: [-120, -1640, 1080], from: [-200, -1500, 400] }, { at: [330, -1420, 860], from: [300, -1300, 240] },
    { at: [-900, -1350, 760], from: [-860, -1280, 230] }, { at: [-1280, -420, 620], from: [-1300, -420, 262] }, { at: [-380, 1380, 760], from: [-430, 1300, 350] }],
};

// lamp posts on the west sidewalk and the string lights from them to the elevated line
export const LAMPS = { x: -(COURT.hx + WEST_WALL.t + 34), ys: [-720, -240, 240, 720], h: 372 };
export const STRINGS = { ys: [-720, -480, -240, 0, 240, 480, 720], h0: 372, h1: 438, sag: 30, bulbEvery: 46 };

// the painted lines (convex polygons [x, y]): boundary, halfway line, centre circle, goal areas, spots
export function courtLines() {
  const out = [], W = 5, bx = COURT.hx - 16, by = COURT.hy - 16;
  const rect = (x0, y0, x1, y1) => out.push([[Math.min(x0, x1), Math.min(y0, y1)], [Math.max(x0, x1), Math.min(y0, y1)], [Math.max(x0, x1), Math.max(y0, y1)], [Math.min(x0, x1), Math.max(y0, y1)]]);
  const ring = (cx, cy, r, a0, a1, segs) => { for (let k = 0; k < segs; k++) { const t0 = a0 + (a1 - a0) * k / segs, t1 = a0 + (a1 - a0) * (k + 1) / segs, p = (rr, t) => [cx + Math.cos(t) * rr, cy + Math.sin(t) * rr]; out.push([p(r - W / 2, t0), p(r + W / 2, t0), p(r + W / 2, t1), p(r - W / 2, t1)]); } };
  rect(-bx, -by, bx, -by + W); rect(-bx, by - W, bx, by); rect(-bx, -by, -bx + W, by); rect(bx - W, -by, bx, by);
  rect(-bx, -W / 2, bx, W / 2); ring(0, 0, 140, 0, Math.PI * 2, 56);
  for (const s of [1, -1]) {
    ring(0, s * by, 250, s > 0 ? Math.PI : 0, s > 0 ? Math.PI * 2 : Math.PI, 36);
    out.push([...Array(10).keys()].map((k) => [Math.cos(k * Math.PI / 5) * 6, s * (by - 330) + Math.sin(k * Math.PI / 5) * 6]));
  }
  return out;
}
