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
  tunnel: { d0: -14, d1: 600, half: 80, top: 80, wall: 8 },
  hall: { d0: 600, d1: 760, half: 420, top: 196 },
  corridor: { d1: 1150, half: 64 },
  rooms: { d0: 776, d1: 1134, w0: 72, w1: 420, door: [880, 960] },
  concourse: { d0: 1150, d1: BACKWALL.d1 - 4, top: 260 },
  doorTop: 112,
};
// gates through the facade: station ranges (w ±175 on the long sides)
export const GATES = [0, N / 4, N / 2, (3 * N) / 4].map((c) => ({ from: c - 2, to: c + 2, centre: c }));
export const TEAMS = [{ i0: 0, side: "east", team: "home" }, { i0: N / 2, side: "west", team: "away" }];

const mod = (i) => ((i % N) + N) % N;
export const inGate = (i) => GATES.some((g) => { const a = mod(i - g.from); return a < g.to - g.from; });

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
    box(WM("floor_rubber"), f, T.d0, T.d1, -T.half, T.half, F, F + 1, 64, ["bottom"]);
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
    ceilingLights(f, H.d0, H.d1, -H.half, H.half, H.top, 80, 140, "0.6", "420");
    // ---- corridor to the concourse, team rooms on both sides
    box(WM("floor_concourse"), f, H.d1, C.d1, -C.half, C.half, F, F + 1, 128, ["bottom"]);
    for (const s of [-1, 1]) wallAlong(WM("wall_white"), f, s > 0 ? C.half : -C.half - 8, H.d1 + 8, C.d1, F, H.top, [R.door]);
    box(WM("ceiling_tile"), f, H.d1, C.d1, -C.half, C.half, H.top, H.top + 8, 64);
    ceilingLights(f, H.d1, C.d1, -C.half, C.half, H.top, 110, 128, "0.45", "300");
    for (const s of [-1, 1]) {
      const w0 = s > 0 ? R.w0 : -R.w1, w1 = s > 0 ? R.w1 : -R.w0;
      const locker = s < 0;   // the locker room is on the -w side; the other room is the warm-up / physio room
      box(WM(locker ? "floor_tile" : "floor_gym"), f, R.d0, R.d1, w0, w1, F, F + 1, 64, ["bottom"]);
      wallAcross(locker ? teamWall : WM("wall_white"), f, R.d0 - 8, w0 - 8, w1 + 8, F, H.top);
      wallAcross(locker ? teamWall : WM("wall_white"), f, R.d1, w0 - 8, w1 + 8, F, H.top);
      box(locker ? teamWall : WM("wall_white"), f, R.d0, R.d1, s > 0 ? R.w1 : -R.w1 - 8, s > 0 ? R.w1 + 8 : -R.w1, F, H.top);
      // the corridor wall (w ±64..72, door at R.door) is the room's inner wall: rooms start at w ±72
      box(WM("ceiling_tile"), f, R.d0 - 8, R.d1 + 8, w0 - 8, w1 + 8, H.top, H.top + 8, 64);
      ceilingLights(f, R.d0, R.d1, w0, w1, H.top, 120, 116, "0.55", "360");
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
    if (i % 6 === 3) lights.push({ origin: P(i, (CC.d0 + CC.d1) / 2, FLOOR + CC.top - 12), brightness: "0.5", range: "420" });
  }
  // the corridor walls stand inside the 208-wide gap of the inner wall: close the rest
  for (const { i0 } of TEAMS) {
    const f = frame(i0), C = INTERIOR.corridor;
    for (const s of [-1, 1]) box(WM("concrete_painted"), f, CC.d0 - 8, CC.d0, s > 0 ? C.half + 8 : -112, s > 0 ? 112 : -C.half - 8, FLOOR, FLOOR + INTERIOR.hall.top);
  }
  // gate jambs: the concourse floor runs out to the facade line, the gate opening is 152 high
  for (const g of GATES) for (const i of [g.from, g.to]) {
    const s = station(i), t = [-s.ny, s.nx, 0];
    const a = P(i, CC.d1, FLOOR), b = P(i, BACKWALL.d1, FLOOR);
    const side = i === g.from ? 1 : -1;
    scene.quad(WM("concrete_painted"), a, b, [b[0], b[1], FLOOR + 152], [a[0], a[1], FLOOR + 152], [[0, 0], [0.3, 0], [0.3, -1.2], [0, -1.2]], vec.mul(t, side), { solid: true });
  }
  // ---- car park west of the stadium: asphalt slab, white bay lines --------------------------------
  {
    const K = CAR_PARK, top = FLOOR + 1.5;
    const wbox = (material, x0, x1, y0, y1, z0, z1, uv = 128, skip = []) =>
      scene.prism(material, [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0]], [[x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]], uv, { solid: true }, skip);
    wbox(WM("asphalt"), K.x1, K.x0, -K.y, K.y, FLOOR, top, 256, ["bottom"]);
    for (const pair of K.pairs) {
      const xa = Math.max(...pair) + K.bay / 2, xb = Math.min(...pair) - K.bay / 2;
      for (const y of carParkBayEdges()) wbox(WM("line_white"), xb, xa, y - 1.5, y + 1.5, top, top + 0.15, 64, ["bottom"]);
      wbox(WM("line_white"), (xa + xb) / 2 - 1.5, (xa + xb) / 2 + 1.5, -K.y + 40, K.y - 40, top, top + 0.15, 64, ["bottom"]);
    }
  }
  return { scene, lights };
}

// Car park: three double rows of bays across the west plaza, a walkway to the west gate in the middle.
export const CAR_PARK = { x0: -3470, x1: -5620, y: 2760, bay: 200, pitch: 112, walk: 260,
  pairs: [[-3740, -3940], [-4460, -4660], [-5180, -5380]] };
export function carParkBayEdges() {
  const K = CAR_PARK, out = [];
  for (let y = K.walk; y + K.pitch <= K.y - 40; y += K.pitch) { out.push(y, -y); }
  const last = out.length ? Math.max(...out) + K.pitch : K.walk; out.push(last, -last);
  return out;
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
    face("tunnel_sign", at(70, 0), outward, I.tunnel.top - 27, false);
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

  // ---- gates: canopy and turnstiles outside every gate, lettered A (east, main) to D --------------
  GATES.forEach((g, k) => {
    const f = frame(g.centre), at = (d, w) => f.at(d, w, FLOOR).slice(0, 2), d0 = BACKWALL.d1;
    face("gate_" + "abcd"[k], at(d0 + 92, 0), f.yaw);
    face("turnstiles", at(d0 + 150, 0), f.yaw);
  });
  {
    const f = frame(0), at = (d, w) => f.at(d, w, FLOOR).slice(0, 2), d0 = BACKWALL.d1;
    for (const w of [-330, 330]) face("ticket_booth", at(d0 + 190, w), f.yaw);
    face("arena_letters", at(d0 + 8, 0), f.yaw, FLOOR + 640, false);
    // flags either side of the main gate, home and away taking turns
    for (const s of [-1, 1]) for (let k = 0; k < 5; k++) face(k % 2 ? "flagpole_away" : "flagpole", at(d0 + 110, s * (480 + k * 90)), f.yaw - 90);
  }

  // ---- east plaza: food street towards the fan shop -------------------------------------------------
  face("hotdog_stand", [3460, 560], 270);
  face("beer_stand", [3460, -560], 90);
  face("pretzel_stand", [3900, 560], 270);
  face("fries_stand", [3900, -560], 90);
  face("merch_shop", [SHOP.x, SHOP.y], SHOP.facing);
  for (const x of [3260, 3680, 4120]) for (const y of [-330, 330]) put("lamp_post", x, y, 90);
  for (const x of [3260, 3680, 4120]) for (const y of [-400, 400]) face("bin", [x + 60, y], y > 0 ? 270 : 90);

  // ---- round the stadium: trees in planters with a bench and a bin, lamps --------------------------
  for (let i = 0; i < RING.N; i += 10) {
    if (GATES.some((g) => Math.abs(((i - g.centre + RING.N / 2) % RING.N + RING.N) % RING.N - RING.N / 2) < 8)) continue;
    const p = P(i, BACKWALL.d1 + 250, FLOOR), s = station(i), outYaw = (Math.atan2(s.ny, s.nx) * 180) / Math.PI;
    if (p[0] < CAR_PARK.x0 + 60) continue;                         // not on the car park
    put("tree", p[0], p[1], i * 37 % 360);
    const t = [-s.ny, s.nx], b = [p[0] + t[0] * 110, p[1] + t[1] * 110];
    face("park_bench", b, outYaw + 180);
    face("bin", [b[0] + t[0] * 70, b[1] + t[1] * 70], outYaw + 180);
    const l = P(i + 5, BACKWALL.d1 + 250, FLOOR);
    if (l[0] >= CAR_PARK.x0 + 60) put("lamp_post", l[0], l[1], outYaw + 90);
  }

  // ---- the car park: about half of the bays taken, mixed models and colours (fixed seed) ------------
  let s = 7;
  const rnd = () => { s = (s * 1103515245 + 12345) % 2147483648; return s / 2147483648; };
  const kinds = ["car_sedan", "car_hatch", "car_suv"], colours = ["", "_red", "_blue", "_white", "_black", "_green", "", "_white", "_black"];
  for (const pair of CAR_PARK.pairs) for (const [k, x] of pair.entries()) {
    for (let y = CAR_PARK.walk + CAR_PARK.pitch / 2; y < CAR_PARK.y - 60; y += CAR_PARK.pitch) for (const sy of [1, -1]) {
      if (rnd() < 0.45) continue;
      const kind = kinds[Math.floor(rnd() * kinds.length)], colour = colours[Math.floor(rnd() * colours.length)];
      // nose towards the aisle, a little jitter
      put(kind + colour, x + (rnd() - 0.5) * 10, sy * y + (rnd() - 0.5) * 8, (k === 0 ? 0 : 180) + (rnd() - 0.5) * 4, false);
    }
  }
  // lamps along the car park's middle lines
  for (const pair of CAR_PARK.pairs) for (let y = -CAR_PARK.y + 300; y < CAR_PARK.y; y += 700) put("lamp_post", (pair[0] + pair[1]) / 2, y, 0);
  return out;
}
