// SoccerMod Arena "stadium world": what lies behind the stands. Players' tunnels behind both tunnel
// heads, the mixed zone, a corridor with the two team rooms on each long side (east = home, west =
// away), the concourse ring under the upper tier with gates to the plaza, and the placed Blender
// props (tools/blender). Nothing here touches the pitch box (±1300 × ±1700).
//
// Local frame per tunnel: d outwards from the front curve (the stands' own d), w along the ring
// (counter-clockwise), z up. Both long sides are straight within |w| < 450 at every d used here
// (the curve bends less than a unit), so rooms are plain boxes in that frame.
import { P, station, RING, FLOOR, BACKWALL } from "./layout.mjs";
import { Scene, vec } from "./lib/mesh.mjs";

const { N } = RING;
const UP = [0, 0, 1], DOWN = [0, 0, -1];
export const WM = (name) => `materials/soccermod_world/${name}.vmat`;

// all numbers of the interior (d, w, z in units)
export const INTERIOR = {
  tunnel: { d0: -10, floor0: -14, d1: 600, half: 80, top: 80, wall: 8 },
  hall: { d0: 600, d1: 760, half: 420, top: 196 },
  corridor: { d1: 1150, half: 64 },
  rooms: { d0: 776, d1: 1134, w0: 72, w1: 420, door: [880, 960] },
  concourse: { d0: 1150, d1: BACKWALL.d1 - 4, top: 260 },
  doorTop: 112,
};
// gates through the facade: station ranges (w ±175 on the long sides)
// 2026-10-18 owner: the west gate (where the car park was) is a closed door - the team entrance
export const GATES = [0, N / 4, N / 2, (3 * N) / 4].map((c, k) => ({ from: c - 2, to: c + 2, centre: c, open: k !== 2, letter: "ABCD"[k] }));
export const TEAMS = [{ i0: 0, side: "east", team: "home" }, { i0: N / 2, side: "west", team: "away" }];

const mod = (i) => ((i % N) + N) % N;
export const inGate = (i) => GATES.some((g) => g.open && mod(i - g.from) < g.to - g.from);

export function frame(i0) {
  const s = station(i0), t = [-s.ny, s.nx, 0], n = [s.nx, s.ny, 0];
  const at = (d, w, z) => { const p = P(i0, d, z); return [p[0] + t[0] * w, p[1] + t[1] * w, z]; };
  return { at, t, n, yaw: (Math.atan2(s.ny, s.nx) * 180) / Math.PI };
}

export function buildWorld() {
  const scene = new Scene(), lights = [];
  const I = INTERIOR, F = FLOOR;
  // axis box in a local frame: d0..d1 × w0..w1 × z0..z1
  const box = (material, f, d0, d1, w0, w1, z0, z1, uv = 64, skip = []) => {
    const c = (d, w, z) => f.at(d, w, z);
    scene.prism(material, [c(d0, w0, z0), c(d1, w0, z0), c(d1, w1, z0), c(d0, w1, z0)], [c(d0, w0, z1), c(d1, w0, z1), c(d1, w1, z1), c(d0, w1, z1)], uv, { solid: true }, skip);
  };
  // wall along w at fixed d (from w0 to w1) or along d at fixed w, with door gaps [a, b] (doorTop high)
  const wallAcross = (material, f, d, w0, w1, z0, z1, gaps = [], t = 8) => {
    let at = w0;
    for (const [a, b] of [...gaps].sort((p, q) => p[0] - q[0])) {
      if (a > at) box(material, f, d, d + t, at, a, z0, z1);
      box(material, f, d, d + t, a, b, I.doorTop + F, z1);
      at = b;
    }
    if (w1 > at) box(material, f, d, d + t, at, w1, z0, z1);
  };
  const wallAlong = (material, f, w, d0, d1, z0, z1, gaps = [], t = 8) => {
    let at = d0;
    for (const [a, b] of [...gaps].sort((p, q) => p[0] - q[0])) {
      if (a > at) box(material, f, at, a, w, w + t, z0, z1);
      box(material, f, a, b, w, w + t, I.doorTop + F, z1);
      at = b;
    }
    if (d1 > at) box(material, f, at, d1, w, w + t, z0, z1);
  };
  const ceilingLights = (f, d0, d1, w0, w1, z, stepD, stepW, brightness, range) => {
    for (let d = d0 + stepD / 2; d < d1; d += stepD)
      for (let w = w0 + stepW / 2; w < w1; w += stepW) {
        box(WM("ceiling_light"), f, d - 14, d + 14, w - 14, w + 14, z - 1, z, 32, ["top"]);
        lights.push({ origin: f.at(d, w, z - 8), brightness, range });
      }
  };

  for (const { i0, team } of TEAMS) {
    const f = frame(i0), T = I.tunnel, H = I.hall, C = I.corridor, R = I.rooms;
    const teamWall = WM(team === "home" ? "wall_home" : "wall_away");
    // ---- players' tunnel: rubber floor, graphite walls with team-coloured light strips, lit ceiling
    box(WM("floor_rubber"), f, T.floor0, T.d1, -T.half, T.half, F, F + 1, 64, ["bottom"]);
    box(WM("tunnel_wall"), f, T.d0, T.d1, T.half, T.half + T.wall, F, T.top);
    box(WM("tunnel_wall"), f, T.d0, T.d1, -T.half - T.wall, -T.half, F, T.top);
    box(WM("tunnel_ceiling"), f, T.d0, T.d1, -T.half - T.wall, T.half + T.wall, T.top, T.top + 8);
    for (const s of [-1, 1]) box(WM(team === "home" ? "strip_home" : "strip_away"), f, T.d0 + 40, T.d1 - 20, s * T.half - (s > 0 ? 1 : 0), s * T.half + (s > 0 ? 0 : 1), F + 54, F + 58, 32);
    ceilingLights(f, T.d0 + 40, T.d1, -20, 20, T.top, 96, 40, "0.5", "260");
    // ---- mixed zone: the wide hall the tunnel starts from
    box(WM("floor_concourse"), f, H.d0, H.d1, -H.half, H.half, F, F + 1, 128, ["bottom"]);
    wallAcross(teamWall, f, H.d0 - 8, -H.half - 8, H.half + 8, F, H.top, [[-T.half, T.half]]);
    box(WM("wall_white"), f, H.d0 - 8, H.d0, -T.half, T.half, T.top + 8, H.top);   // over the tunnel mouth
    wallAcross(WM("wall_white"), f, H.d1, -H.half - 8, H.half + 8, F, H.top, [[-C.half, C.half]]);
    for (const s of [-1, 1]) box(WM("wall_white"), f, H.d0, H.d1, s > 0 ? H.half : -H.half - 8, s > 0 ? H.half + 8 : -H.half, F, H.top);
    box(WM("ceiling_tile"), f, H.d0 - 8, H.d1 + 8, -H.half - 8, H.half + 8, H.top, H.top + 8, 64);
    ceilingLights(f, H.d0, H.d1, -H.half, H.half, H.top, 160, 210, "0.08", "300");
    // ---- corridor to the concourse, team rooms on both sides
    box(WM("floor_concourse"), f, H.d1, C.d1, -C.half, C.half, F, F + 1, 128, ["bottom"]);
    for (const s of [-1, 1]) wallAlong(WM("wall_white"), f, s > 0 ? C.half : -C.half - 8, H.d1 + 8, C.d1, F, H.top, [R.door]);
    box(WM("ceiling_tile"), f, H.d1, C.d1, -C.half, C.half, H.top, H.top + 8, 64);
    ceilingLights(f, H.d1, C.d1, -C.half, C.half, H.top, 110, 128, "0.12", "240");
    for (const s of [-1, 1]) {
      const w0 = s > 0 ? R.w0 : -R.w1, w1 = s > 0 ? R.w1 : -R.w0;
      const locker = s < 0;   // the locker room is on the -w side; the other room is the warm-up / physio room
      box(WM(locker ? "floor_tile" : "floor_gym"), f, R.d0, R.d1, w0, w1, F, F + 1, 64, ["bottom"]);
      wallAcross(locker ? teamWall : WM("wall_white"), f, R.d0 - 8, w0 - 8, w1 + 8, F, H.top);
      wallAcross(locker ? teamWall : WM("wall_white"), f, R.d1, w0 - 8, w1 + 8, F, H.top);
      box(locker ? teamWall : WM("wall_white"), f, R.d0, R.d1, s > 0 ? R.w1 : -R.w1 - 8, s > 0 ? R.w1 + 8 : -R.w1, F, H.top);
      // the corridor wall (w ±64..72, door at R.door) is the room's inner wall: rooms start at w ±72
      box(WM("ceiling_tile"), f, R.d0 - 8, R.d1 + 8, w0 - 8, w1 + 8, H.top, H.top + 8, 64);
      ceilingLights(f, R.d0, R.d1, w0, w1, H.top, 120, 116, "0.15", "280");
    }
  }

  // ---- concourse ring: floor, ceiling, inner wall (pitch side) and the inside of the facade -------
  const CC = INTERIOR.concourse;
  const ringQuad = (material, i, d0, z0, d1, z1, facing, scale = 128) => {
    const pts = [P(i, d0, z0), P(i + 1, d0, z0), P(i + 1, d1, z1), P(i, d1, z1)];
    const wall = Math.abs(z1 - z0) > Math.abs(d1 - d0);
    const uv = pts.map((p) => (wall ? [(p[0] + p[1]) / scale, -p[2] / scale] : [p[0] / scale, -p[1] / scale]));
    scene.quad(material, ...pts, uv, facing, { solid: true });
  };
  const corridorGap = (i) => TEAMS.some(({ i0 }) => mod(i - i0 + 1) < 2);   // stations -1..1 round a tunnel
  for (let i = 0; i < N; i++) {
    const s = station(i), out = [s.nx, s.ny, 0], inw = [-s.nx, -s.ny, 0];
    ringQuad(WM("floor_concourse"), i, CC.d0, FLOOR + 1, CC.d1, FLOOR + 1, UP);
    ringQuad(WM("ceiling_tile"), i, CC.d0, FLOOR + CC.top, CC.d1, FLOOR + CC.top, DOWN);
    ringQuad(WM("concrete_painted"), i, CC.d0, FLOOR + CC.top, CC.d0, FLOOR, out);   // inner wall seen from the concourse
    if (corridorGap(i)) ringQuad(WM("concrete_painted"), i, CC.d0, FLOOR + CC.top, CC.d0, FLOOR + INTERIOR.hall.top, out);
    if (!inGate(i)) ringQuad(WM("concourse_back"), i, CC.d1, FLOOR, CC.d1, FLOOR + CC.top, inw);
    else ringQuad(WM("concourse_back"), i, CC.d1, FLOOR + 152, CC.d1, FLOOR + CC.top, inw);
    if (i % 6 === 3) lights.push({ origin: P(i, (CC.d0 + CC.d1) / 2, FLOOR + CC.top - 12), brightness: "0.35", range: "420" });
  }
  // the corridor walls stand inside the 208-wide gap of the inner wall: close the rest
  for (const { i0 } of TEAMS) {
    const f = frame(i0), C = INTERIOR.corridor;
    for (const s of [-1, 1]) box(WM("concrete_painted"), f, CC.d0 - 8, CC.d0, s > 0 ? C.half + 8 : -112, s > 0 ? 112 : -C.half - 8, FLOOR, FLOOR + INTERIOR.hall.top);
  }
  // gate jambs: the concourse floor runs out to the facade line, the gate opening is 152 high
  for (const g of GATES.filter((x) => x.open)) for (const i of [g.from, g.to]) {
    const s = station(i), t = [-s.ny, s.nx, 0];
    const a = P(i, CC.d1, FLOOR), b = P(i, BACKWALL.d1, FLOOR);
    const side = i === g.from ? 1 : -1;
    scene.quad(WM("concrete_painted"), a, b, [b[0], b[1], FLOOR + 152], [a[0], a[1], FLOOR + 152], [[0, 0], [0.3, 0], [0.3, -1.2], [0, -1.2]], vec.mul(t, side), { solid: true });
  }
  outside(scene);
  return { scene, lights };
}



// ---- placed Blender props (tools/blender/props/<name>.py -> models/soccermod_world/<name>.vmdl) ----
export const PROP = (name) => `models/soccermod_world/${name}.vmdl`;
// The fan shop: the spot where a customer stands at its counter and the way he faces (the plugin's
// ArenaShopCounter, tools/arena/generate-arena-plugin.mjs).
export const SHOP = { x: 4400, y: 0, facing: 180 };
export const SHOP_COUNTER = { x: SHOP.x + 30, y: SHOP.y, z: FLOOR, yaw: 0 };

export function placedProps() {
  const out = [];
  // cars are modelled front +X: raw yaw. Everything else is modelled front -Y: face(...) turns that
  // front towards `facing` (degrees, world).
  const put = (name, x, y, yaw = 0, shadows = true, z = FLOOR) => out.push({ model: PROP(name), origin: [x, y, z].map((v) => Number(v.toFixed(2))), yaw: Number(yaw.toFixed(2)), shadows });
  const face = (name, [x, y], facing, z = FLOOR, shadows = true) => put(name, x, y, facing + 90, shadows, z);
  const I = INTERIOR;

  // ---- behind the stands, per team (east home, west away): local frame d / w ----------------------
  for (const { i0, team } of TEAMS) {
    const f = frame(i0), at = (d, w) => f.at(d, w, FLOOR).slice(0, 2), sfx = team === "away" ? "_away" : "";
    const toPitch = f.yaw + 180, outward = f.yaw, plusW = f.yaw + 90, minusW = f.yaw - 90;
    // players' tunnel: the sign over the exit (read when walking out), crests either side of the mouth
    face("tunnel_sign", at(70, 0), outward, I.tunnel.top - 15, false);   // bottom edge 12 over a player
    for (const w of [-200, 200]) face("crest" + sfx, at(I.hall.d0 + 1, w), outward, FLOOR + 120, false);
    // locker room (-w side): lockers along the back wall and the side wall, tactics board, crest
    const R = I.rooms;
    for (const w of [-R.w1 + 90, -R.w1 + 240]) face("locker_bank" + sfx, at(R.d1 - 32, w), toPitch);
    for (const d of [R.d0 + 90, R.d0 + 240]) face("locker_bank" + sfx, at(d, -R.w1 + 32), plusW);
    face("tactics_board", at(R.d0 + 70, -R.w0 - 120), outward);
    face("crest" + sfx, at(R.d0 + 1, -R.w0 - 240), outward, FLOOR + 130, false);
    // warm-up / physio room (+w side)
    for (const d of [R.d0 + 80, R.d0 + 200]) face("massage_table", at(d, R.w1 - 70), minusW);
    for (const w of [R.w0 + 80, R.w0 + 150, R.w0 + 220]) face("exercise_bike" + sfx, at(R.d1 - 60, w), outward + 90);
    // concourse: bins along the outer wall
    for (const w of [-300, 300]) face("bin" + sfx, at(I.concourse.d1 - 20, w), toPitch);
  }


  // ---- gates: canopy, turnstiles and ticket booths at the open gates; the west one is the closed
  // team entrance with the team buses and the TV trucks in front of it -----------------------------
  for (const g of GATES) {
    const f = frame(g.centre), at = (d, w) => f.at(d, w, FLOOR).slice(0, 2), d0 = BACKWALL.d1;
    if (!g.open) { face("team_entrance", at(d0 + 2, 0), f.yaw); continue; }
    face("gate_" + g.letter.toLowerCase(), at(d0 + 92, 0), f.yaw);
    face("turnstiles", at(d0 + 150, 0), f.yaw);
    for (const w of [-330, 330]) face("ticket_booth", at(d0 + 190, w), f.yaw);
    face("info_pylon", at(d0 + 300, -520), f.yaw);
    for (const w of [-260, -200, 200, 260]) put("bollard", ...at(d0 + 360, w), 0);
  }
  {
    const f = frame(0), at = (d, w) => f.at(d, w, FLOOR).slice(0, 2), d0 = BACKWALL.d1;
    face("arena_letters", at(d0 + 8, 0), f.yaw, FLOOR + 640, false);
    for (const s of [-1, 1]) for (let k = 0; k < 5; k++) face(k % 2 ? "flagpole_away" : "flagpole", at(d0 + 110, s * (480 + k * 90)), f.yaw - 90);
  }

  // ---- east: food street towards the fan shop ------------------------------------------------------
  face("hotdog_stand", [3460, 560], 270);
  face("beer_stand", [3460, -560], 90);
  face("pretzel_stand", [3900, 560], 270);
  face("fries_stand", [3900, -560], 90);
  face("merch_shop", [SHOP.x, SHOP.y], SHOP.facing);
  for (const x of [3260, 3680, 4120]) for (const y of [-330, 330]) put("lamp_post", x, y, 90);
  for (const x of [3260, 3680, 4120]) for (const y of [-400, 400]) face("bin", [x + 60, y], y > 0 ? 270 : 90);

  // ---- north: fan zone with the big screen, beer tables, food ----------------------------------------
  face("fan_screen", [0, 5350], 270);
  for (let r = 0; r < 4; r++) for (let c = -3; c <= 3; c++) face("beer_table", [c * 260, 4250 + r * 230], 90 * (r % 2 ? 1 : -1) + 90);
  face("hotdog_stand", [-1350, 4500], 0);
  face("beer_stand", [1350, 4500], 180);
  face("fries_stand", [-1350, 4950], 0);
  face("pretzel_stand", [1350, 4950], 180);
  for (const x of [-1000, 1000]) for (const y of [4100, 4700, 5300]) put("lamp_post", x, y, 0);
  for (let k = 0; k < 8; k++) face(k % 2 ? "flagpole_away" : "flagpole", [-1750 + k * 500, 5850], 270);

  // ---- west: team entrance, team buses, TV trucks, barriers -----------------------------------------
  face("team_bus", [-3700, 700], 270);
  face("team_bus_away", [-3700, -700], 90);
  face("ob_van", [-4500, 1500], 0);
  face("ob_van", [-4500, -1500], 0);
  for (const s of [-1, 1]) for (let k = 0; k < 7; k++) face("barrier", [-3020 - k * 88, s * 280], s > 0 ? 270 : 90);
  for (const s of [-1, 1]) for (let k = 0; k < 4; k++) face("barrier", [-3700 - (k - 1.5) * 88, s * 1000], s > 0 ? 270 : 90);

  // ---- south: monument, food trucks, bike racks ------------------------------------------------------
  face("statue_ball", [0, -4350], 90);
  face("food_truck", [-1250, -4500], 90);
  face("food_truck_blue", [1250, -4500], 90);
  face("food_truck_green", [0, -5150], 90);
  for (let k = 0; k < 4; k++) face("bike_rack", [-700 + k * 120, -3800], 90);
  for (let k = 0; k < 4; k++) face("bike_rack", [340 + k * 120, -3800], 90);
  for (const x of [-800, 800]) for (const y of [-4100, -4800]) put("lamp_post", x, y, 0);

  // ---- round the stadium: lawns with trees and benches, lamps, banners on the facade ----------------
  for (let i = 0; i < RING.N; i += 4) {
    const near = GATES.some((g) => Math.abs(((i - g.centre + RING.N / 2) % RING.N + RING.N) % RING.N - RING.N / 2) < LAWN.gap + 2);
    const s = station(i), outYaw = (Math.atan2(s.ny, s.nx) * 180) / Math.PI;
    // banners hang on the facade all round, home and away taking turns
    if (i % 8 === 0 && !GATES.some((g) => Math.abs(((i - g.centre + RING.N / 2) % RING.N + RING.N) % RING.N - RING.N / 2) < 4))
      face(i % 16 === 0 ? "facade_banner" : "facade_banner_away", P(i, BACKWALL.d1 + 3, FLOOR).slice(0, 2), outYaw, FLOOR + 230, false);
    if (near) continue;
    const t = [-s.ny, s.nx];
    const lawn = P(i, (LAWN.d0 + LAWN.d1) / 2, FLOOR);
    put("tree_free", lawn[0] + t[0] * 40, lawn[1] + t[1] * 40, i * 37 % 360, true, FLOOR + LAWN.h);
    if (i % 8 === 0) {
      const b = P(i, LAWN.d1 + 70, FLOOR);
      face("park_bench", [b[0], b[1]], outYaw);
      face("bin", [b[0] + t[0] * 75, b[1] + t[1] * 75], outYaw);
    } else {
      const l = P(i, LAWN.d0 - 60, FLOOR);
      put("lamp_post", l[0], l[1], outYaw + 90);
    }
  }
  for (const [sx, sy] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) {
    const K = CORNER_PARK, cx = sx * (K.x0 + K.x1) / 2, cy = sy * (K.y0 + K.y1) / 2;
    for (const i of [-1, 0, 1]) for (const j of [-1, 0, 1]) if (i || j) put("tree_free", cx + i * 380, cy + j * 380, (i * 47 + j * 91 + 360) % 360, true, FLOOR + 4);
    for (const s of [-1, 1]) { face("park_bench", [cx + s * 150, cy], s > 0 ? 0 : 180, FLOOR + 4); face("bin", [cx + s * 150, cy + 80], 0, FLOOR + 4); }
    put("lamp_post", cx, cy + 190, 0, true, FLOOR + 4);
  }
  for (let i = 2; i < RING.N; i += 8) {
    if (GATES.some((g) => Math.abs(((i - g.centre + RING.N / 2) % RING.N + RING.N) % RING.N - RING.N / 2) < 9)) continue;
    const p = P(i, OUTSIDE.inlays[1] + 110, FLOOR), s = station(i), outYaw = (Math.atan2(s.ny, s.nx) * 180) / Math.PI;
    if (Math.abs(p[0]) > CORNER_PARK.x0 - 150 && Math.abs(p[1]) > CORNER_PARK.y0 - 150) continue;
    if (Math.abs(p[1]) > 3900 && Math.abs(p[0]) < 1900) continue;     // fan zone and the monument square
    if (p[0] < -3000 && Math.abs(p[1]) < 1800) continue;                 // team buses and TV trucks
    if (p[0] > 3000 && Math.abs(p[1]) < 800) continue;                   // food street
    put("tree", p[0], p[1], i * 53 % 360);
    const l = P(i + 4, OUTSIDE.inlays[1] + 110, FLOOR);
    put("lamp_post", l[0], l[1], outYaw + 90);
  }
  // the boundary: a row of trees outside the hedge
  for (let x = -OUTSIDE.edge.x + 150; x <= OUTSIDE.edge.x - 150; x += 420) for (const y of [-1, 1]) put("tree_free", x, y * (OUTSIDE.edge.y + 120), (x * 7) % 360, false);
  for (let y = -OUTSIDE.edge.y + 150; y <= OUTSIDE.edge.y - 150; y += 420) for (const x of [-1, 1]) put("tree_free", x * (OUTSIDE.edge.x + 120), y, (y * 3) % 360, false);

  // ---- inside the stadium: corner flags, TV cameras, ball boy stools, substitution board -------------
  for (const x of [-1, 1]) for (const y of [-1, 1]) put("corner_flag" + (y > 0 ? "" : "_away"), x * 1272, y * 1656, 0, false);
  for (const [x, y, facing] of [[1460, -700, 180], [1460, 700, 180], [-1460, 1300, 0], [1460, 300, 180], [0, 1930, 270], [0, -1930, 90]]) face("tv_camera", [x, y], facing);
  for (const [x, y, facing] of [[1330, -1300, 180], [1330, 1300, 180], [-1330, -1100, 0], [-1330, 1100, 0], [700, 1740, 270], [-700, -1740, 90]]) face("ballboy_stool", [x, y], facing);
  face("fourth_official", [-1470, 0], 0);
  return out;
}

// ---- the plaza: granite apron at the facade, lawns with kerbs between the gates, stone inlay rings,
// a medallion in front of every open gate, a hedge round the edge ------------------------------------
export const LAWN = { d0: 1560, d1: 1830, h: 2, gap: 12 };
export const CORNER_PARK = { x0: 4200, x1: 5400, y0: 4700, y1: 5900 };
export const OUTSIDE = { apron: 110, inlays: [2040, 2720], medallion: { d: 430, r: 180 }, edge: { x: 5640, y: 6150 } };
function outside(scene) {
  const up = [0, 0, 1];
  const nearGate = (i, n) => GATES.some((g) => Math.abs(((i - g.centre + N / 2) % N + N) % N - N / 2) < n);
  // a raised band round the ring between d0 and d1, h high, with its kerbs
  const band = (material, d0, d1, h, filter = () => true, kerb = material, scale = 256) => {
    for (let i = 0; i < N; i++) {
      if (!filter(i)) continue;
      const a = P(i, d0, FLOOR + h), b = P(i + 1, d0, FLOOR + h), c = P(i + 1, d1, FLOOR + h), d = P(i, d1, FLOOR + h);
      scene.quad(material, a, b, c, d, [a, b, c, d].map((p) => [p[0] / scale, -p[1] / scale]), up, { solid: true });
      const s = station(i), out = [s.nx, s.ny, 0], inw = [-s.nx, -s.ny, 0];
      const wall = (p, q, facing) => scene.quad(kerb, [p[0], p[1], FLOOR], [q[0], q[1], FLOOR], q, p, [[0, 0], [0.5, 0], [0.5, h / 64], [0, h / 64]], facing, { solid: true });
      wall(a, b, inw); wall(d, c, out);
      if (!filter(i - 1)) scene.quad(kerb, [a[0], a[1], FLOOR], [d[0], d[1], FLOOR], d, a, [[0, 0], [0.5, 0], [0.5, h / 64], [0, h / 64]], [s.ny, -s.nx, 0], { solid: true });
      if (!filter(i + 1)) scene.quad(kerb, [b[0], b[1], FLOOR], [c[0], c[1], FLOOR], c, b, [[0, 0], [0.5, 0], [0.5, h / 64], [0, h / 64]], [-s.ny, s.nx, 0], { solid: true });
    }
  };
  band(WM("plaza_granite"), BACKWALL.d1, BACKWALL.d1 + OUTSIDE.apron, 0.8, () => true, WM("plaza_granite"), 128);
  const lawns = (i) => !nearGate(i, LAWN.gap);
  band(WM("lawn_grass"), LAWN.d0, LAWN.d1, LAWN.h, lawns, WM("plaza_granite"), 192);
  band(WM("plaza_granite"), LAWN.d0 - 10, LAWN.d0, LAWN.h + 2, lawns, WM("plaza_granite"), 64);
  band(WM("plaza_granite"), LAWN.d1, LAWN.d1 + 10, LAWN.h + 2, lawns, WM("plaza_granite"), 64);
  for (const d of OUTSIDE.inlays) band(WM("plaza_inlay"), d, d + 24, 0.5, () => true, WM("plaza_inlay"), 128);
  // medallions in front of the open gates
  for (const g of GATES.filter((x) => x.open)) {
    const f = frame(g.centre), c = f.at(BACKWALL.d1 + OUTSIDE.medallion.d, 0, FLOOR + 0.9), R = OUTSIDE.medallion.r, n = 48;
    const rim = [], disc = [];
    for (let k = 0; k < n; k++) {
      const a = (k / n) * 2 * Math.PI, ca = Math.cos(a), sa = Math.sin(a);
      disc.push([c[0] + ca * R, c[1] + sa * R, c[2]]);
      rim.push([ca, sa]);
    }
    // the picture is upright for someone walking towards the gate
    const rot = ((f.yaw + 90) * Math.PI) / 180, uv = rim.map(([ca, sa]) => [0.5 + 0.5 * (ca * Math.cos(rot) + sa * Math.sin(rot)), 0.5 - 0.5 * (-ca * Math.sin(rot) + sa * Math.cos(rot))]);
    scene.poly(WM("plaza_medallion"), disc, uv, up, { solid: true });
  }
  // a small park in every corner of the plaza (big empty squares otherwise)
  for (const [sx, sy] of [[1, 1], [-1, 1], [1, -1], [-1, -1]]) {
    const P0 = CORNER_PARK, xs = [sx * P0.x0, sx * P0.x1].sort((a, b) => a - b), ys = [sy * P0.y0, sy * P0.y1].sort((a, b) => a - b);
    const slab = (m, x0, x1, y0, y1, h, uv) => scene.prism(m, [[x0, y0, FLOOR], [x1, y0, FLOOR], [x1, y1, FLOOR], [x0, y1, FLOOR]], [[x0, y0, FLOOR + h], [x1, y0, FLOOR + h], [x1, y1, FLOOR + h], [x0, y1, FLOOR + h]], uv, { solid: true }, ["bottom"]);
    slab(WM("plaza_granite"), xs[0] - 12, xs[1] + 12, ys[0] - 12, ys[1] + 12, 3, 64);
    slab(WM("lawn_grass"), xs[0], xs[1], ys[0], ys[1], 4, 192);
  }
  // the city: apartment blocks outside the hedge close the world off (only their inner faces show)
  {
    const S = { x: 5888, y: 6400 }, D = 80;
    let seed = 3; const rnd = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
    const block = (x0, x1, y0, y1, h) => scene.prism(WM("city_facade"), [[x0, y0, FLOOR], [x1, y0, FLOOR], [x1, y1, FLOOR], [x0, y1, FLOOR]], [[x0, y0, FLOOR + h], [x1, y0, FLOOR + h], [x1, y1, FLOOR + h], [x0, y1, FLOOR + h]], 700, { solid: true }, ["bottom"]);
    for (const sy of [-1, 1]) for (let x = -S.x; x < S.x;) { const w = 380 + rnd() * 420, h = 520 + Math.round(rnd() * 5) * 175; const y0 = sy > 0 ? S.y - D - 8 : -S.y + 8; block(x, Math.min(S.x, x + w), y0, y0 + D, h); x += w; }
    for (const sx of [-1, 1]) for (let y = -S.y + D + 8; y < S.y - D - 8;) { const w = 380 + rnd() * 420, h = 520 + Math.round(rnd() * 5) * 175; const x0 = sx > 0 ? S.x - D - 8 : -S.x + 8; block(x0, x0 + D, y, Math.min(S.y - D - 8, y + w), h); y += w; }
  }
  // hedge round the edge of the plaza
  const E = OUTSIDE.edge, T = 50, H = 80;
  const hedge = (x0, y0, x1, y1) => scene.prism(WM("hedge"), [[x0, y0, FLOOR], [x1, y0, FLOOR], [x1, y1, FLOOR], [x0, y1, FLOOR]], [[x0, y0, FLOOR + H], [x1, y0, FLOOR + H], [x1, y1, FLOOR + H], [x0, y1, FLOOR + H]], 96, { solid: true }, ["bottom"]);
  hedge(-E.x - T, E.y, E.x + T, E.y + T); hedge(-E.x - T, -E.y - T, E.x + T, -E.y);
  hedge(E.x, -E.y, E.x + T, E.y); hedge(-E.x - T, -E.y, -E.x, E.y);
}
