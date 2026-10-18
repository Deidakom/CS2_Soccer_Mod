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
    const tEnd = H.d0 - 8;   // stops at the mixed zone wall instead of running into it
    box(WM("tunnel_wall"), f, T.d0, tEnd, T.half, T.half + T.wall, F, T.top);
    box(WM("tunnel_wall"), f, T.d0, tEnd, -T.half - T.wall, -T.half, F, T.top);
    box(WM("tunnel_ceiling"), f, T.d0, tEnd, -T.half - T.wall, T.half + T.wall, T.top, T.top + 8);
    for (const s of [-1, 1]) box(WM(team === "home" ? "strip_home" : "strip_away"), f, T.d0 + 40, T.d1 - 20, s * T.half - (s > 0 ? 1 : 0), s * T.half + (s > 0 ? 0 : 1), F + 54, F + 58, 32);
    ceilingLights(f, T.d0 + 40, T.d1, -20, 20, T.top, 96, 40, "0.5", "260");
    // 2026-10-18 owner ("the tunnel looks so empty"): handrails, floor light lines in the team colour
    for (const s of [-1, 1]) {
      const w = s * (T.half - 3);
      box(WM("steel_brushed"), f, 20, tEnd - 20, w - 1, w + 1, F + 34, F + 36, 32);
      for (let d = 30; d < tEnd - 20; d += 90) box(WM("steel_brushed"), f, d - 1, d + 1, s > 0 ? w : w - 2, s > 0 ? w + 2 : w, F + 30, F + 35, 16);
      box(WM(team === "home" ? "strip_home" : "strip_away"), f, T.floor0 + 4, tEnd, s * T.half - (s > 0 ? 6 : -2), s * T.half - (s > 0 ? 2 : -6), F + 1, F + 1.4, 32);
    }
    // ---- mixed zone: the wide hall the tunnel starts from
    box(WM("floor_concourse"), f, H.d0, H.d1, -H.half, H.half, F, F + 1, 128, ["bottom"]);
    wallAcross(teamWall, f, H.d0 - 8, -H.half - 8, H.half + 8, F, H.top, [[-T.half, T.half]]);
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
    // 2026-10-18 owner ("no pictures on the walls, no details, no plants"): dress every room
    const hang = (name, d, w, z, facing) => face(name, at(d, w), facing, FLOOR + z, false);
    const H = I.hall, T = I.tunnel;
    // players' tunnel: crowd murals in light boxes on both walls
    for (const d of [120, 300, 470]) { hang("mural_panel", d, T.half, 72, minusW); hang("mural_panel", d, -T.half, 72, plusW); }
    // mixed zone: interview backdrop, trophy cabinet, pictures, clock, plants, exit sign
    face("media_wall", at(H.d1 - 40, -260), toPitch);
    face("trophy_cabinet", at(H.d1 - 20, 280), toPitch);
    hang("frame_net", H.d0 + 1, -330, 110, outward); hang("frame_stadium", H.d0 + 1, 330, 120, outward);
    hang("wall_clock", H.d0 + 1, 0, 150, outward);
    hang("exit_sign", H.d1 - 1, 0, 128, toPitch);
    for (const w of [-400, 400]) { face("plant_big", at(H.d0 + 24, w), outward); face("plant_big", at(H.d1 - 24, w > 0 ? 100 : -100), outward); }
    hang("fire_extinguisher", H.d0 + 1, 120, 40, outward);
    // locker room (-w side)
    hang(team === "home" ? "poster_red" : "poster_blue", R.d0 + 1, -R.w0 - 40, 110, outward);
    hang("wall_tv", R.d0 + 1, -R.w0 - 160, 140, outward);
    hang(team === "home" ? "frame_action" : "frame_fans", R.d0 + 1, -R.w1 + 50, 120, outward);
    hang("frame_vintage", R.d1 - 120, -R.w0 - 1, 120, minusW);
    hang("wall_clock", 920, -R.w0 - 1, 150, minusW);
    face("locker_bench", at(1000, -250), plusW);
    face("kit_hamper" + sfx, at(R.d0 + 40, -R.w1 + 120), outward);
    face("water_cooler", at(R.d1 - 30, -R.w0 - 30), toPitch);
    face("ball_bag", at(860, -R.w0 - 40), plusW);
    hang("exit_sign", 920, -R.w0 - 1, 128, minusW);
    // warm-up / physio room (+w side)
    face("weight_rack", at(R.d0 + 24, 300), outward);
    for (const w of [R.w0 + 70, R.w0 + 150]) face("treadmill", at(R.d0 + 70, w), toPitch);
    face("ice_bath", at(1000, R.w0 + 70), outward);
    face("yoga_mats", at(900, 230), outward);
    face("physio_cart", at(R.d1 - 30, R.w1 - 34), toPitch);
    face("water_crate", at(R.d1 - 30, R.w0 + 30), toPitch);
    hang("frame_stadium", R.d0 + 1, 250, 120, outward);
    hang("wall_tv", R.d1 - 1, 120, 150, toPitch);
    hang(team === "home" ? "poster_blue" : "poster_red", R.d0 + 1, R.w1 - 50, 110, outward);
    hang("fire_extinguisher", 960, R.w0 + 1, 40, plusW);
    face("plant_big", at(R.d0 + 30, R.w1 - 28), outward);
    // the pitch side of the tunnel: extending tunnel, kit for the bench
    face("tunnel_canopy" + sfx, at(-160, 0), toPitch);
    face("water_crate", at(-120, 160), toPitch);
    face("ball_bag", at(-200, -170), toPitch);
    face("cone_stack", at(-240, 230), toPitch);
    face("physio_cart", at(-120, -260), toPitch);
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

  // ---- round the stadium: a green belt with trees, benches and lamps; banners on the facade ---------
  for (let i = 0; i < RING.N; i += 4) {
    const near = GATES.some((g) => Math.abs(((i - g.centre + RING.N / 2) % RING.N + RING.N) % RING.N - RING.N / 2) < LAWN.gap + 2);
    const s = station(i), outYaw = (Math.atan2(s.ny, s.nx) * 180) / Math.PI;
    if (i % 8 === 0 && !GATES.some((g) => Math.abs(((i - g.centre + RING.N / 2) % RING.N + RING.N) % RING.N - RING.N / 2) < 4))
      face(i % 16 === 0 ? "facade_banner" : "facade_banner_away", P(i, BACKWALL.d1 + 3, FLOOR).slice(0, 2), outYaw, FLOOR + 230, false);
    if (near) continue;
    const t = [-s.ny, s.nx], lawn = P(i, (LAWN.d0 + LAWN.d1) / 2, FLOOR);
    put("tree_free", lawn[0] + t[0] * 40, lawn[1] + t[1] * 40, i * 37 % 360, true, FLOOR + LAWN.h);
    if (i % 8 === 4) { const l = P(i, LAWN.d0 - 50, FLOOR); put("lamp_post", l[0], l[1], outYaw + 90); }
  }

  // ---- the playground (owner 2026-10-18: "a playground outside with loads of options, all working") --
  {
    const G = PLAYGROUND;
    // 1v1 cage at the size of soccer_1v1_cage, long side along Y
    put("cage_pool", G.cage.c[0], G.cage.c[1], 0, true, FLOOR + 3);
    // penalty shootout: goal at the south end, spot 11 m (430 units) out
    face("goal_full", [G.shootout.c[0], G.shootout.c[1] - 300], 90, FLOOR + 3);
    // crossbar challenge: lit crossbar at the north end, shooting line 18 m out
    face("goal_full_lit", [G.crossbar.c[0], G.crossbar.c[1] + 360], 270, FLOOR + 3);
    // beach volleyball: court long axis along X, the net across it
    put("volley_net", G.volley.c[0], G.volley.c[1], 90, true, FLOOR + 6);
    // basketball: hoop at the north end, the rim facing the court
    put("bball_hoop", G.basket.c[0], G.basket.c[1] + 268, 0, true, FLOOR + 2);
    // Torwand
    face("torwand", [G.torwand.c[0], G.torwand.c[1] - 180], 90, FLOOR + 3);
    face("cone_stack", [G.torwand.c[0] - 220, G.torwand.c[1] + 200], 90, FLOOR + 3);
    // jump pads round the giant ball
    for (const [dx, dy] of JUMP_PADS) put("jump_pad", G.jump.c[0] + dx, G.jump.c[1] + dy, 0, false, FLOOR + 2);
    // a sign, two benches and two lamps at every field
    for (const [k, f] of Object.entries(G)) {
      const [cx, cy] = f.c, out = Math.abs(cx) > Math.abs(cy) * 0.8 ? [Math.sign(cx), 0] : [0, Math.sign(cy)];
      const along = [out[1], -out[0]], half = k === "cage" ? 380 : 340;
      const sp = [cx - out[0] * 0 + along[0] * half, cy + along[1] * half];
      put("playground_sign", sp[0], sp[1], (Math.atan2(-along[1], -along[0]) * 180) / Math.PI + 90);
      for (const s of [-1, 1]) {
        const b = [cx + along[0] * s * (half + 60) + out[0] * 120, cy + along[1] * s * (half + 60) + out[1] * 120];
        face("park_bench", b, (Math.atan2(-along[1] * s, -along[0] * s) * 180) / Math.PI);
        put("lamp_post", cx + along[0] * s * (half + 40) - out[0] * 200, cy + along[1] * s * (half + 40) - out[1] * 200, 0);
      }
    }
    face("bin", [G.volley.c[0] + 470, G.volley.c[1] + 120], 0);
    face("bin", [G.basket.c[0] - 360, G.basket.c[1] + 120], 0);
  }

  // ---- the boundary: a row of trees outside the hedge -------------------------------------------------
  for (let x = -OUTSIDE.edge.x + 150; x <= OUTSIDE.edge.x - 150; x += 420) for (const y of [-1, 1]) put("tree_free", x, y * (OUTSIDE.edge.y + 110), (x * 7) % 360, false);
  for (let y = -OUTSIDE.edge.y + 150; y <= OUTSIDE.edge.y - 150; y += 420) for (const x of [-1, 1]) put("tree_free", x * (OUTSIDE.edge.x + 110), y, (y * 3) % 360, false);

  // ---- inside the stadium: TV cameras, ball boy stools (owner 2026-10-18: no corner flags) ----------
  for (const [x, y, facing] of [[1460, -700, 180], [1460, 700, 180], [-1460, 1300, 0], [1460, 300, 180], [0, 1930, 270], [0, -1930, 90]]) face("tv_camera", [x, y], facing);
  for (const [x, y, facing] of [[1330, -1300, 180], [1330, 1300, 180], [-1330, -1100, 0], [-1330, 1100, 0], [700, 1740, 270], [-700, -1740, 90]]) face("ballboy_stool", [x, y], facing);
  return out;
}

// ---- the plaza (owner 2026-10-18: "more compact around the stadium") --------------------------------
export const WORLD_SKY = { x: 4300, y: 4800 };
export const LAWN = { d0: 1440, d1: 1560, h: 2, gap: 12 };
export const OUTSIDE = { apron: 110, inlays: [1600], medallion: { d: 300, r: 150 }, edge: { x: WORLD_SKY.x - 220, y: WORLD_SKY.y - 220 } };
// the playground fields (centres, world units)
export const PLAYGROUND = {
  cage: { c: [3600, 1150] }, shootout: { c: [3600, -1150] },
  crossbar: { c: [-3600, 1150] }, golf: { c: [-3600, -1150] },
  volley: { c: [-1250, 4050] }, basket: { c: [1250, 4050] },
  torwand: { c: [-1150, -4050] }, jump: { c: [1150, -4050] },
};
export const JUMP_PADS = [[-200, -150], [200, -150], [0, 200]];
export const GOLF = { lanes: 6, len: 360, width: 70, gap: 20, kerb: 8, rise: 12 };
const golfLaneX = (k) => PLAYGROUND.golf.c[0] + (k - (GOLF.lanes - 1) / 2) * (GOLF.width + GOLF.gap);
// playable balls (prop_physics sm_fun_<kind>_<n>, the plugin makes them kickable: FunBalls.cs)
export function funBalls() {
  const G = PLAYGROUND, M = (n) => PROP(n), z = (h) => FLOOR + h;
  const list = [
    ["cage", "ball_match", [G.cage.c[0], G.cage.c[1], z(20)]],
    ["volley", "ball_volley", [G.volley.c[0] - 200, G.volley.c[1], z(20)]], ["volley", "ball_volley", [G.volley.c[0] + 200, G.volley.c[1], z(20)]],
    ["basket", "ball_basket", [G.basket.c[0] - 40, G.basket.c[1], z(20)]], ["basket", "ball_basket", [G.basket.c[0] + 40, G.basket.c[1] - 60, z(20)]],
    ...[-30, 0, 30].map((dx) => ["torwand", "ball_fun", [G.torwand.c[0] + dx, G.torwand.c[1] + 250, z(20)]]),
    ...[-20, 20].map((dx) => ["shootout", "ball_match", [G.shootout.c[0] + dx, G.shootout.c[1] + 130, z(20)]]),
    ...[-40, 0, 40].map((dx) => ["crossbar", "ball_match", [G.crossbar.c[0] + dx, G.crossbar.c[1] - 340, z(20)]]),
    ...Array.from({ length: GOLF.lanes }, (_, k) => ["golf", "ball_golf", [golfLaneX(k), PLAYGROUND.golf.c[1] - GOLF.len / 2 + 30, z(GOLF.rise + 8)]]),
    ["giant", "ball_giant", [G.jump.c[0], G.jump.c[1], z(70)]],
  ];
  const n = {};
  return list.map(([kind, model, origin]) => ({ name: `sm_fun_${kind}_${(n[kind] = (n[kind] ?? 0) + 1)}`, model: M(model), origin }));
}
// jump pad pushes (trigger_push brushes, apply-arena-vmap.mjs): { box: [x0, y0, z0, x1, y1, z1], props }
export function triggers() {
  return JUMP_PADS.map(([dx, dy]) => {
    const x = PLAYGROUND.jump.c[0] + dx, y = PLAYGROUND.jump.c[1] + dy;
    return { box: [x - 34, y - 34, FLOOR + 2, x + 34, y + 34, FLOOR + 40], props: { classname: "trigger_push", pushdir: "-90 0 0", speed: "900", spawnflags: "1", StartDisabled: "0" } };
  });
}

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
  const slab = (m, x0, x1, y0, y1, h, uv, z0 = FLOOR, skip = ["bottom"]) => scene.prism(m, [[x0, y0, z0], [x1, y0, z0], [x1, y1, z0], [x0, y1, z0]], [[x0, y0, z0 + h], [x1, y0, z0 + h], [x1, y1, z0 + h], [x0, y1, z0 + h]], uv, { solid: true }, skip);
  const rect = (m, c, hw, hh, h, uv, kerb = WM("plaza_granite")) => { slab(kerb, c[0] - hw - 14, c[0] + hw + 14, c[1] - hh - 14, c[1] + hh + 14, h - 1, 64); slab(m, c[0] - hw, c[0] + hw, c[1] - hh, c[1] + hh, h, uv); };
  const line = (x0, x1, y0, y1, z) => slab(WM("line_white"), Math.min(x0, x1), Math.max(x0, x1), Math.min(y0, y1), Math.max(y0, y1), 0.3, 64, z);

  band(WM("plaza_granite"), BACKWALL.d1, BACKWALL.d1 + OUTSIDE.apron, 0.8, () => true, WM("plaza_granite"), 128);
  const lawns = (i) => !nearGate(i, LAWN.gap);
  band(WM("lawn_grass"), LAWN.d0, LAWN.d1, LAWN.h, lawns, WM("plaza_granite"), 192);
  band(WM("plaza_granite"), LAWN.d0 - 10, LAWN.d0, LAWN.h + 2, lawns, WM("plaza_granite"), 64);
  band(WM("plaza_granite"), LAWN.d1, LAWN.d1 + 10, LAWN.h + 2, lawns, WM("plaza_granite"), 64);
  for (const d of OUTSIDE.inlays) band(WM("plaza_inlay"), d, d + 24, 0.5, () => true, WM("plaza_inlay"), 128);
  for (const g of GATES.filter((x) => x.open)) {
    const f = frame(g.centre), c = f.at(BACKWALL.d1 + OUTSIDE.medallion.d, 0, FLOOR + 0.9), R = OUTSIDE.medallion.r, n = 48;
    const disc = [], rim = [];
    for (let k = 0; k < n; k++) { const a = (k / n) * 2 * Math.PI; disc.push([c[0] + Math.cos(a) * R, c[1] + Math.sin(a) * R, c[2]]); rim.push([Math.cos(a), Math.sin(a)]); }
    const rot = ((f.yaw + 90) * Math.PI) / 180, uv = rim.map(([ca, sa]) => [0.5 + 0.5 * (ca * Math.cos(rot) + sa * Math.sin(rot)), 0.5 - 0.5 * (-ca * Math.sin(rot) + sa * Math.cos(rot))]);
    scene.poly(WM("plaza_medallion"), disc, uv, up, { solid: true });
  }

  // ---- playground grounds ----------------------------------------------------------------------------
  const G = PLAYGROUND;
  rect(WM("lawn_grass"), G.cage.c, 340, 560, 3, 128);                                 // under the 1v1 cage
  rect(WM("lawn_grass"), G.shootout.c, 300, 380, 3, 128);
  slab(WM("line_white"), G.shootout.c[0] - 6, G.shootout.c[0] + 6, G.shootout.c[1] + 130 - 6, G.shootout.c[1] + 130 + 6, 0.3, 64, FLOOR + 3);
  line(G.shootout.c[0] - 220, G.shootout.c[0] + 220, G.shootout.c[1] - 300 + 200, G.shootout.c[1] - 300 + 203, FLOOR + 3);
  rect(WM("lawn_grass"), G.crossbar.c, 300, 420, 3, 128);
  line(G.crossbar.c[0] - 200, G.crossbar.c[0] + 200, G.crossbar.c[1] - 340 - 2, G.crossbar.c[1] - 340 + 2, FLOOR + 3);
  // beach volleyball (long axis X): sand in a timber frame, tape lines
  rect(WM("sand"), G.volley.c, 410, 250, 6, 160, WM("locker_wood"));
  for (const sx of [-1, 1]) line(G.volley.c[0] + sx * 312, G.volley.c[0] + sx * 314, G.volley.c[1] - 162, G.volley.c[1] + 162, FLOOR + 6);
  for (const sy of [-1, 1]) line(G.volley.c[0] - 314, G.volley.c[0] + 314, G.volley.c[1] + sy * 160, G.volley.c[1] + sy * 162, FLOOR + 6);
  // basketball half court, hoop at the north end
  rect(WM("court"), G.basket.c, 300, 290, 2, 192);
  {
    const B = G.basket.c, hy = B[1] + 250, bz = FLOOR + 2;
    line(B[0] - 280, B[0] + 280, B[1] - 273, B[1] - 270, bz);
    for (const sx of [-1, 1]) line(B[0] + sx * 64, B[0] + sx * 66, hy - 200, hy + 20, bz);
    line(B[0] - 66, B[0] + 66, hy - 200, hy - 198, bz);
    for (let k = 0; k < 24; k++) {
      const a0 = Math.PI * (1.1 + 0.8 * k / 24), a1 = Math.PI * (1.1 + 0.8 * (k + 1) / 24), r = 262, r2 = 259;
      const p = (a, rr) => [B[0] + Math.cos(a) * rr, hy + Math.sin(a) * rr, bz + 0.3];
      scene.quad(WM("line_white"), p(a0, r), p(a1, r), p(a1, r2), p(a0, r2), [[0, 0], [1, 0], [1, 1], [0, 1]], up, {});
    }
  }
  rect(WM("lawn_grass"), G.torwand.c, 260, 330, 3, 128);
  slab(WM("line_white"), G.torwand.c[0] - 6, G.torwand.c[0] + 6, G.torwand.c[1] + 250 - 6, G.torwand.c[1] + 250 + 6, 0.3, 64, FLOOR + 3);
  rect(WM("court"), G.jump.c, 320, 320, 2, 192);
  // mini golf: six raised lanes with kerbs, a cup at the far end of each, a bump, a block or a slanted wall
  {
    const K = GOLF, y0 = G.golf.c[1] - K.len / 2, y1 = G.golf.c[1] + K.len / 2, top = K.rise, hole = 10;
    rect(WM("plaza_granite"), G.golf.c, (K.lanes * (K.width + K.gap)) / 2 + 30, K.len / 2 + 40, 1, 64);
    for (let k = 0; k < K.lanes; k++) {
      const x = golfLaneX(k), xa = x - K.width / 2, xb = x + K.width / 2, cy = y1 - 50;
      // green with a square cup: four slabs round the hole, the cup floor 9 units down
      slab(WM("lawn_grass"), xa, xb, y0, cy - hole, top, 96);
      slab(WM("lawn_grass"), xa, xb, cy + hole, y1, top, 96);
      slab(WM("lawn_grass"), xa, x - hole, cy - hole, cy + hole, top, 96);
      slab(WM("lawn_grass"), x + hole, xb, cy - hole, cy + hole, top, 96);
      slab(WM("rubber_black"), x - hole, x + hole, cy - hole, cy + hole, top - 9, 32);
      slab(WM("flag_home"), x - 0.8, x + 0.8, cy + hole - 3, cy + hole - 1.4, 60, 16, FLOOR + top);
      for (const sx of [-1, 1]) slab(WM("locker_wood"), sx > 0 ? xb : xa - K.kerb, sx > 0 ? xb + K.kerb : xa, y0 - K.kerb, y1 + K.kerb, top + 8, 64);
      slab(WM("locker_wood"), xa, xb, y1, y1 + K.kerb, top + 8, 64);
      slab(WM("locker_wood"), xa, xb, y0 - K.kerb, y0, top + 8, 64);
      const z = FLOOR + top;
      if (k % 3 === 0) scene.prism(WM("lawn_grass"), [[xa, y0 + 140, z], [xb, y0 + 140, z], [xb, y0 + 200, z], [xa, y0 + 200, z]], [[xa, y0 + 165, z + 7], [xb, y0 + 165, z + 7], [xb, y0 + 175, z + 7], [xa, y0 + 175, z + 7]], 96, { solid: true }, ["bottom"]);
      else if (k % 3 === 1) slab(WM("locker_wood"), x - 14, x + 14, y0 + 160, y0 + 180, 14, 32, z);
      else scene.prism(WM("locker_wood"), [[xa, y0 + 150, z], [xa + 40, y0 + 190, z], [xa + 46, y0 + 184, z], [xa + 6, y0 + 144, z]], [[xa, y0 + 150, z + 14], [xa + 40, y0 + 190, z + 14], [xa + 46, y0 + 184, z + 14], [xa + 6, y0 + 144, z + 14]], 32, { solid: true }, ["bottom"]);
    }
  }

  // the city: apartment blocks outside the hedge close the world off (only their inner faces show)
  {
    const S = WORLD_SKY, D = 80;
    let seed = 3; const rnd = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647; };
    const block = (x0, x1, y0, y1, h) => scene.prism(WM("city_facade"), [[x0, y0, FLOOR], [x1, y0, FLOOR], [x1, y1, FLOOR], [x0, y1, FLOOR]], [[x0, y0, FLOOR + h], [x1, y0, FLOOR + h], [x1, y1, FLOOR + h], [x0, y1, FLOOR + h]], 700, { solid: true }, ["bottom"]);
    for (const sy of [-1, 1]) for (let x = -S.x; x < S.x;) { const w = 380 + rnd() * 420, h = 520 + Math.round(rnd() * 5) * 175; const y0 = sy > 0 ? S.y - D - 8 : -S.y + 8; block(x, Math.min(S.x, x + w), y0, y0 + D, h); x += w; }
    for (const sx of [-1, 1]) for (let y = -S.y + D + 8; y < S.y - D - 8;) { const w = 380 + rnd() * 420, h = 520 + Math.round(rnd() * 5) * 175; const x0 = sx > 0 ? S.x - D - 8 : -S.x + 8; block(x0, x0 + D, y, Math.min(S.y - D - 8, y + w), h); y += w; }
  }
  // hedge round the edge of the plaza
  const E = OUTSIDE.edge, T = 50, Hh = 80;
  const hedge = (x0, y0, x1, y1) => scene.prism(WM("hedge"), [[x0, y0, FLOOR], [x1, y0, FLOOR], [x1, y1, FLOOR], [x0, y1, FLOOR]], [[x0, y0, FLOOR + Hh], [x1, y0, FLOOR + Hh], [x1, y1, FLOOR + Hh], [x0, y1, FLOOR + Hh]], 96, { solid: true }, ["bottom"]);
  hedge(-E.x - T, E.y, E.x + T, E.y + T); hedge(-E.x - T, -E.y - T, E.x + T, -E.y);
  hedge(E.x, -E.y, E.x + T, E.y); hedge(-E.x - T, -E.y, -E.x, E.y);
}
