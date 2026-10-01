#!/usr/bin/env node
// SoccerMod indoor hall: every texture and material, generated. Pictures with lettering come from
// render-hall-graphics.ps1 (run that first; it also needs the stadium's ads_a.png / ads_b.png).
//
//   node tools/hall/generate-hall-textures.mjs --graphics <dir> [--addon <content addon dir>] [--preview <dir>] [--only a,b]
// Writes materials/soccermod_hall/<name>.vmat + _color / _normal / _trans / _illum .png into the
// addon, and the same pictures plus materials.json for the local viewer into the preview dir.
import fs from "node:fs";
import path from "node:path";
import { Img, readPng, makeNoise, mix, smooth, gridLine } from "../arena/lib/img.mjs";
import { bladeField, grassImage } from "../arena/lib/grass.mjs";
import { shade, concrete, riser, stair, metalPanels, seat, seatFrame, ledBand, vipGlass, vipInterior, concourse, ceilingLights, roofCeiling, steel, truss, pitchLine, glassRail } from "../arena/lib/recipes.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.graphics) throw new Error("usage: --graphics <dir> [--addon <dir>] [--preview <dir>] [--only a,b]");
const DIR = "materials/soccermod_hall";
const only = args.only?.split(",");

// ---- the hall's own recipes ------------------------------------------------------------------------
const flat = (rgb) => ({ color: new Img(16, 16).fill(() => rgb) });
const picture = (name) => ({ color: readPng(path.join(args.graphics, `${name}.png`)) });
// Indoor turf, drawn blade by blade like the stadium's pitch: 2048 px per 170.67 units = 12 texels
// per unit, so it stays sharp right under the camera. Two tones with the blades leaning opposite ways.
const turf = (base, lean, seed) => () => grassImage(2048, bladeField(2048, () => lean, 520000, seed), () => base, seed + 300);
// an LED board: the picture as round dots on a dark panel (4 px per LED)
function ledBoard(name) {
  const src = readPng(path.join(args.graphics, `${name}.png`)), img = new Img(src.w, src.h), illum = new Img(src.w, src.h);
  const dot = (x, y) => smooth(1.9, 1.1, Math.hypot((x % 4) - 1.5, (y % 4) - 1.5));
  img.fill((x, y) => { const c = src.get(x - (x % 4) + 1, y - (y % 4) + 1), d = dot(x, y); return [c[0] * (0.2 + 0.8 * d), c[1] * (0.2 + 0.8 * d), c[2] * (0.2 + 0.8 * d)]; });
  illum.fill((x, y) => { const d = dot(x, y) * 255; return [d, d, d]; });
  return { color: img, illum };
}
function boardCap() {
  const s = 32, img = new Img(s, s), illum = new Img(s, s), line = (y) => y >= 5 && y <= 10;
  img.fill((x, y) => (line(y) ? [200, 240, 255] : shade([40, 42, 48], 1 - (y / s) * 0.2)));
  illum.fill((x, y) => (line(y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, illum };
}
function acrylic() {
  const w = 256, h = 256, img = new Img(w, h), alpha = new Img(w, h), n = makeNoise(401);
  const frame = (x, y) => gridLine(x, w) < 3 || y < 3 || y > h - 4;
  img.fill((x, y, u, v) => (frame(x, y) ? [70, 74, 80] : shade([190, 214, 226], 1 + (n.fbm(u, v, 3, 3) - 0.5) * 0.1)));
  alpha.fill((x, y, u, v) => (frame(x, y) ? [235, 235, 235] : shade([34, 34, 34], 1 + (n.fbm(u, v, 5, 3) - 0.5) * 0.5 + smooth(0.75, 1, v) * 0.5)));
  return { color: img, alpha };
}
// ball-stop net: a square mesh of white cord, knots a little brighter
function net(strength = 150) {
  const s = 128, cell = 32, img = new Img(s, s), alpha = new Img(s, s);
  const d = (x, y) => Math.min(gridLine(x, cell), gridLine(y, cell)), knot = (x, y) => Math.hypot(gridLine(x, cell), gridLine(y, cell));
  img.fill((x, y) => shade([206, 210, 214], knot(x, y) < 3 ? 1.08 : 0.94));
  alpha.fill((x, y) => { const a = Math.max(smooth(1.7, 0.7, d(x, y)), smooth(3.2, 2, knot(x, y))) * strength; return [a, a, a]; });
  return { color: img, alpha };
}
function goalPad() {
  const s = 128, n = makeNoise(411), img = new Img(s, s), seam = (x, y) => Math.min(gridLine(x + y, 64), gridLine(x - y, 64));
  img.fill((x, y, u, v) => shade([30, 31, 35], 1 + (n.fbm(u, v, 6, 3) - 0.5) * 0.14 - smooth(3, 0.5, seam(x, y)) * 0.35 + smooth(9, 3, seam(x, y)) * 0.08));
  return { color: img, height: (x, y) => -smooth(4, 0.5, seam(x, y)) * 3 };
}
function rubberFloor() {
  const s = 512, n = makeNoise(421), img = new Img(s, s);
  img.fill((x, y, u, v) => {
    const speck = n(u, v, 256), speck2 = n(u + 0.37, v + 0.11, 256);
    let c = shade([62, 64, 70], 1 + (n.fbm(u, v, 4, 3) - 0.5) * 0.12 + (speck - 0.5) * 0.1);
    if (speck > 0.86) c = mix(c, [150, 154, 160], 0.55); else if (speck2 > 0.9) c = mix(c, [40, 96, 160], 0.5);
    return c;
  });
  return { color: img, height: (x, y, u, v) => n(u, v, 256) * 0.8 };
}
// acoustic wall: anthracite panels with fine vertical grooves
function acousticPanel() {
  const s = 256, n = makeNoise(431), img = new Img(s, s), groove = (x) => gridLine(x, 16), joint = (x, y) => Math.min(gridLine(x, s), gridLine(y, s));
  img.fill((x, y, u, v) => shade([48, 50, 56], 1 + (n.fbm(u, v, 5, 3) - 0.5) * 0.1 - smooth(2.2, 0.4, groove(x)) * 0.4 - smooth(2.5, 0.5, joint(x, y)) * 0.4));
  return { color: img, height: (x, y) => -smooth(3, 0.4, groove(x)) * 3 - smooth(3, 0.5, joint(x, y)) * 2 };
}
// timber slats: warm oak battens on black felt
function timberSlats() {
  const s = 512, n = makeNoise(441), img = new Img(s, s), W = 32;
  img.fill((x, y, u, v) => {
    const slat = Math.floor(x / W), inX = x % W, gap = inX < 6, tone = 0.86 + ((slat * 37) % 11) / 11 * 0.22;
    if (gap) return [14, 13, 13];
    const grain = (n.fbm(u * 0.25 + slat * 0.13, v, 60, 3, 0.06) - 0.5) * 0.3 + (n(u * 3, v, 2, 180) - 0.5) * 0.08, edge = smooth(4, 0, Math.min(inX - 6, W - 1 - inX)) * 0.22;
    return shade([178, 128, 76], tone * (1 + grain) - edge);
  });
  return { color: img, height: (x) => ((x % W) < 6 ? -6 : 0) };
}
function windows() {
  const w = 256, h = 64, img = new Img(w, h), illum = new Img(w, h), bar = (x, y) => gridLine(x, w / 4) < 3 || y < 4 || y > h - 5;
  img.fill((x, y, u, v) => (bar(x, y) ? [38, 40, 44] : mix([206, 228, 248], [246, 250, 255], 1 - v)));
  illum.fill((x, y) => (bar(x, y) ? [0, 0, 0] : [255, 255, 255]));
  return { color: img, illum };
}
function skylight() {
  const s = 256, n = makeNoise(451), img = new Img(s, s), illum = new Img(s, s), bar = (x, y) => gridLine(x, s / 2) < 4 || gridLine(y, s) < 5;
  img.fill((x, y, u, v) => (bar(x, y) ? [222, 224, 228] : shade([214, 236, 252], 1 + (n.fbm(u, v, 3, 3) - 0.5) * 0.06)));
  illum.fill((x, y) => (bar(x, y) ? [40, 40, 40] : [255, 255, 255]));
  return { color: img, illum };
}
function ledBar() {
  const w = 256, h = 32, img = new Img(w, h), illum = new Img(w, h), lit = (x, y) => y > 4 && y < h - 5 && x > 5 && x < w - 6 && gridLine(x, w / 4) > 2;
  img.fill((x, y) => (lit(x, y) ? [255, 252, 244] : [34, 36, 40]));
  illum.fill((x, y) => (lit(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, illum };
}
function cubeBottom() {
  const s = 256, img = new Img(s, s), illum = new Img(s, s), ring = (x, y) => { const r = Math.hypot(x - s / 2, y - s / 2); return (r > 84 && r < 98) || (Math.max(Math.abs(x - s / 2), Math.abs(y - s / 2)) > 116 && Math.max(Math.abs(x - s / 2), Math.abs(y - s / 2)) < 122); };
  img.fill((x, y) => (ring(x, y) ? [220, 244, 255] : [22, 23, 27]));
  illum.fill((x, y) => (ring(x, y) ? [255, 255, 255] : [0, 0, 0]));
  return { color: img, illum };
}

// ---- materials ----------------------------------------------------------------------------------------
// rough / metal: constants; alphaTest: cut-out; translucent: blended (alpha picture);
// illum: self-illumination brightness (with the mask when the recipe has one, else the whole picture)
const MATERIALS = {
  turf_a: { make: turf([56, 98, 40], Math.PI * 0.5, 501), rough: 0.95, normal: 1.1, noShadow: true, surface: "Grass" },
  turf_b: { make: turf([64, 110, 45], Math.PI * 1.5, 502), rough: 0.95, normal: 1.1, noShadow: true, surface: "Grass" },
  line: { make: pitchLine, rough: 1, noShadow: true },
  collide: { make: () => flat([255, 0, 255]), rough: 1 },
  board_led: { make: () => ledBoard("ads_b"), rough: 0.35, illum: 2.2 },
  board_back: { make: () => metalPanels({ size: 256, base: [44, 46, 52], seed: 511, cols: 2, rows: 1 }), rough: 0.5, metal: 0.3, normal: 1 },
  board_cap: { make: boardCap, rough: 0.4, metal: 0.3, illum: 3 },
  glass: { make: acrylic, rough: 0.06, translucent: true, twoSided: true, noShadow: true },
  net: { make: () => net(150), rough: 0.9, translucent: true, twoSided: true, noShadow: true },
  net_top: { make: () => net(95), rough: 0.9, translucent: true, twoSided: true, noShadow: true },
  steel_dark: { make: () => steel([42, 44, 48], 82), rough: 0.5, metal: 0.3 },
  steel_white: { make: () => steel([214, 217, 222], 81), rough: 0.5, metal: 0.15, noShadow: true },
  post_white: { make: () => steel([240, 240, 238], 84), rough: 0.35, metal: 0.1 },
  goal_face: { make: () => metalPanels({ size: 256, base: [30, 32, 37], seed: 521, cols: 2, rows: 2 }), rough: 0.45, metal: 0.4, normal: 1 },
  goal_trim_red: { make: () => flat([255, 70, 60]), rough: 0.4, illum: 3.2, illumAll: true, noShadow: true },
  goal_trim_blue: { make: () => flat([70, 130, 255]), rough: 0.4, illum: 3.2, illumAll: true, noShadow: true },
  goal_pad: { make: goalPad, rough: 0.85, normal: 1 },
  goal_shell: { make: () => metalPanels({ size: 256, base: [46, 48, 55], seed: 522, cols: 2, rows: 2 }), rough: 0.5, metal: 0.35, normal: 1 },
  walkway: { make: rubberFloor, rough: 0.8, normal: 0.8 },
  wall_base: { make: () => concrete({ base: [150, 150, 147], seed: 12, joints: 4, jointsV: 2, ties: true, streaks: 0.6 }), rough: 0.9, normal: 1.4 },
  wall_dark: { make: acousticPanel, rough: 0.85, normal: 1 },
  wall_timber: { make: timberSlats, rough: 0.6, normal: 1.2 },
  mural: { make: () => picture("hall_mural"), rough: 0.6, illum: 0.5, illumAll: true },
  windows: { make: windows, rough: 0.2, illum: 3 },
  video: { make: () => picture("hall_video"), rough: 0.3, illum: 2.4, illumAll: true },
  banner_red: { make: () => picture("hall_banner_red"), rough: 0.9, twoSided: true },
  banner_blue: { make: () => picture("hall_banner_blue"), rough: 0.9, twoSided: true },
  concrete: { make: () => concrete({ base: [172, 170, 164], seed: 11, joints: 2, jointsV: 2, stain: 0.5 }), rough: 0.92, normal: 1.2 },
  riser: { make: riser, rough: 0.92 },
  stair: { make: stair, rough: 0.88 },
  seat_red: { make: () => seat([196, 30, 34]), rough: 0.42, alphaTest: 0.5, twoSided: true },
  seat_blue: { make: () => seat([30, 70, 176]), rough: 0.42, alphaTest: 0.5, twoSided: true },
  seat_dark: { make: () => seat([92, 98, 110]), rough: 0.42, alphaTest: 0.5, twoSided: true },
  seat_white: { make: () => seat([228, 228, 224]), rough: 0.42, alphaTest: 0.5, twoSided: true },
  seat_frame: { make: seatFrame, rough: 0.5, metal: 0.3, alphaTest: 0.5, twoSided: true },
  ads: { make: () => picture("ads_a"), rough: 0.5 },
  rail_glass: { make: glassRail, rough: 0.08, translucent: true, twoSided: true, noShadow: true },
  concourse: { make: concourse, rough: 0.8, illum: 1.5 },
  lounge_soffit: { make: ceilingLights, rough: 0.8, illum: 2.2 },
  fascia_led: { make: ledBand, rough: 0.25, illum: 2.5 },
  lounge_interior: { make: vipInterior, rough: 0.8, illum: 1.6, illumAll: true, noShadow: true },
  lounge_glass: { make: vipGlass, rough: 0.08, translucent: true, twoSided: true, noShadow: true },
  sign_lounge: { make: () => picture("hall_sign"), rough: 0.4, illum: 2.5, illumAll: true, twoSided: true },
  roof_deck: { make: roofCeiling, rough: 0.75, normal: 1 },
  skylight: { make: skylight, rough: 0.2, illum: 2.6, noShadow: true },
  truss: { make: truss, rough: 0.5, metal: 0.15, alphaTest: 0.5, twoSided: true, noShadow: true },
  led_bar: { make: ledBar, rough: 0.3, illum: 6, noShadow: true },
  cube_bottom: { make: cubeBottom, rough: 0.4, illum: 4, noShadow: true },
};

const f3 = (v) => v.toFixed(6);
function vmat(name, m, has) {
  const lines = ['\t"shader"\t"csgo_complex.vfx"'];
  if (m.alphaTest) lines.push('\t"F_ALPHA_TEST"\t"1"');
  if (m.translucent) lines.push('\t"F_TRANSLUCENT"\t"1"');
  if (m.illum) lines.push('\t"F_SELF_ILLUM"\t"1"');
  if (m.twoSided) lines.push('\t"F_RENDER_BACKFACES"\t"1"');
  if (m.noShadow) lines.push('\t"F_DO_NOT_CAST_SHADOWS"\t"1"');
  if (m.alphaTest) lines.push(`\t"g_flAlphaTestReference"\t"${m.alphaTest.toFixed(3)}"`);
  lines.push(`\t"g_flMetalness"\t"${(m.metal ?? 0).toFixed(3)}"`);
  if (m.illum) lines.push('\t"g_flSelfIllumAlbedoFactor"\t"1.000"', `\t"g_flSelfIllumBrightness"\t"${m.illum.toFixed(3)}"`, '\t"g_flSelfIllumScale"\t"1.000"', '\t"g_vSelfIllumTint"\t"[1.000000 1.000000 1.000000 0.000000]"');
  lines.push(`\t"TextureColor"\t"${DIR}/${name}_color.png"`);
  if (has.normal) lines.push(`\t"TextureNormal"\t"${DIR}/${name}_normal.png"`);
  if (has.alpha) lines.push(`\t"TextureTranslucency"\t"${DIR}/${name}_trans.png"`);
  if (m.illum) lines.push(has.illum ? `\t"TextureSelfIllumMask"\t"${DIR}/${name}_illum.png"` : '\t"TextureSelfIllumMask"\t"[1.000000 1.000000 1.000000 0.000000]"');
  lines.push(`\t"TextureRoughness"\t"[${f3(m.rough)} ${f3(m.rough)} ${f3(m.rough)} 0.000000]"`);
  if (m.surface) lines.push('\t"SystemAttributes"', "\t{", `\t\t"PhysicsSurfaceProperties"\t"${m.surface}"`, "\t}");
  return `"Layer0"\n{\n${lines.join("\n")}\n}\n`;
}

const outDirs = [args.addon && path.join(args.addon, DIR), args.preview && path.join(args.preview, "tex")].filter(Boolean);
for (const d of outDirs) fs.mkdirSync(d, { recursive: true });
const previewFile = args.preview && path.join(args.preview, "materials.json");
const preview = previewFile && fs.existsSync(previewFile) && only ? JSON.parse(fs.readFileSync(previewFile, "utf8")) : {};
for (const [name, m] of Object.entries(MATERIALS)) {
  if (only && !only.includes(name)) continue;
  const r = m.make(), has = { normal: !!(m.normal && r.height), alpha: !!r.alpha, illum: !!r.illum && !m.illumAll };
  const normal = has.normal ? r.color.setHeight(r.height).normalMap(m.normal) : null;
  for (const d of outDirs) {
    r.color.png(path.join(d, `${name}_color.png`), 3);
    if (normal) normal.png(path.join(d, `${name}_normal.png`), 3);
    if (r.alpha) r.alpha.png(path.join(d, `${name}_trans.png`), 1);
    if (has.illum) r.illum.png(path.join(d, `${name}_illum.png`), 1);
  }
  if (args.addon) fs.writeFileSync(path.join(args.addon, DIR, `${name}.vmat`), vmat(name, m, has));
  preview[name] = { map: `${name}_color.png`, normal: normal ? `${name}_normal.png` : undefined, alpha: r.alpha ? `${name}_trans.png` : undefined,
    alphaTest: m.alphaTest, translucent: m.translucent, opacity: m.translucent ? 1 : undefined, emissive: m.illum ? Math.min(1.6, m.illum / 3) : undefined, emissiveMask: has.illum ? `${name}_illum.png` : undefined,
    roughness: m.rough, metalness: m.metal ?? 0, twoSided: m.twoSided, noShadow: m.noShadow };
  console.log(`${name}: ${r.color.w}x${r.color.h}${normal ? " +normal" : ""}${r.alpha ? " +alpha" : ""}${has.illum ? " +illum" : ""}`);
}
if (previewFile) fs.writeFileSync(previewFile, JSON.stringify(preview, null, 1));
