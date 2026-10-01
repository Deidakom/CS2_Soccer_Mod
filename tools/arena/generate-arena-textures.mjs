#!/usr/bin/env node
// SoccerMod Arena: every texture and material of the new stadium, generated from scratch.
// The pictures with lettering come from render-arena-graphics.ps1 (run that first).
//
//   node tools/arena/generate-arena-textures.mjs --graphics <dir> [--addon <content addon dir>] [--preview <dir>]
// Writes materials/soccermod_arena/<name>.vmat + _color / _normal / _trans / _illum .png into the
// addon, and the same pictures plus materials.json for the local viewer into the preview dir.
import fs from "node:fs";
import path from "node:path";
import { Img, readPng, makeNoise, mix, clamp01, smooth, gridLine, roundRect } from "./lib/img.mjs";
import { bladeField, grassImage, GRASS_DARK, GRASS_MID, GRASS_LIGHT } from "./lib/grass.mjs";

const args = Object.fromEntries(process.argv.slice(2).reduce((p, a, i, all) => { if (a.startsWith("--")) p.push([a.slice(2), all[i + 1]]); return p; }, []));
if (!args.graphics) throw new Error("usage: --graphics <dir> [--addon <dir>] [--preview <dir>]");
const DIR = "materials/soccermod_arena";
const only = args.only?.split(",");

// ---- recipes -------------------------------------------------------------------------------------
// Each returns { color: Img, height?: fn, alpha?: Img (grey), illum?: Img (grey) }.
import { shade, concrete, riser, stair, metalPanels, turf, tarmac, pavers, seat, seatFrame, ledBand, vipGlass, vipInterior, concourse, ceilingLights, portal, roofMetal, roofCeiling, roofGlass, steel, truss, floodlight, facade, facadeBase, pitchGrass, pitchLine, ringLeds, glassRail, shutter } from "./lib/recipes.mjs";
const flat = (rgb) => ({ color: new Img(16, 16).fill(() => rgb) });
const picture = (name) => ({ color: readPng(path.join(args.graphics, `${name}.png`)) });

// ---- materials ----------------------------------------------------------------------------------------
// rough / metal: constants; alphaTest: cut-out; translucent: blended (alpha picture);
// illum: self-illumination brightness (with the mask when the recipe has one, else the whole picture)
const MATERIALS = {
  concrete: { make: () => concrete({ base: [172, 170, 164], seed: 11, joints: 2, jointsV: 2, stain: 0.7 }), rough: 0.92, normal: 1.2 },
  concrete_wall: { make: () => concrete({ base: [150, 150, 147], seed: 12, joints: 4, jointsV: 2, ties: true, streaks: 1.1 }), rough: 0.9, normal: 1.4 },
  riser: { make: riser, rough: 0.92 },
  stair: { make: stair, rough: 0.88 },
  parapet_cap: { make: () => metalPanels({ size: 128, base: [58, 60, 66], seed: 21, cols: 1, rows: 1 }), rough: 0.45, metal: 0.4 },
  ads_a: { make: () => picture("ads_a"), rough: 0.5 },
  ads_b: { make: () => picture("ads_b"), rough: 0.5 },
  surround_turf: { make: () => turf([52, 104, 52], 51), rough: 0.98, normal: 1 },
  apron: { make: tarmac, rough: 0.9, normal: 1 },
  plaza: { make: pavers, rough: 0.9, normal: 1.2 },
  seat_red: { make: () => seat([196, 30, 34]), rough: 0.42, alphaTest: 0.5, twoSided: true },
  seat_blue: { make: () => seat([30, 70, 176]), rough: 0.42, alphaTest: 0.5, twoSided: true },
  seat_dark: { make: () => seat([92, 98, 110]), rough: 0.42, alphaTest: 0.5, twoSided: true },
  seat_white: { make: () => seat([228, 228, 224]), rough: 0.42, alphaTest: 0.5, twoSided: true },
  seat_frame: { make: seatFrame, rough: 0.5, metal: 0.3, alphaTest: 0.5, twoSided: true },
  fascia: { make: () => metalPanels({ base: [50, 53, 60], seed: 61, cols: 2, rows: 4 }), rough: 0.5, metal: 0.35, normal: 1 },
  led_band: { make: ledBand, rough: 0.25, illum: 2.5 },
  vip_glass: { make: vipGlass, rough: 0.08, translucent: true, twoSided: true, noShadow: true },
  vip_interior: { make: vipInterior, rough: 0.8, illum: 1.6, noShadow: true },
  concourse: { make: concourse, rough: 0.8, illum: 1.5 },
  ceiling_lights: { make: ceilingLights, rough: 0.8, illum: 2.2 },
  dark: { make: () => flat([20, 21, 24]), rough: 0.8 },
  soffit: { make: () => metalPanels({ size: 128, base: [128, 131, 136], seed: 71, cols: 1, rows: 1 }), rough: 0.7 },
  portal: { make: portal, rough: 0.85, illum: 4 },
  roof_metal: { make: roofMetal, rough: 0.45, metal: 0.3, normal: 1 },
  roof_ceiling: { make: roofCeiling, rough: 0.75, normal: 1 },
  roof_glass: { make: roofGlass, rough: 0.15, translucent: true, twoSided: true, noShadow: true },
  steel_white: { make: () => steel([210, 213, 218], 81), rough: 0.5, metal: 0.15 },
  steel_roof: { make: () => steel([210, 213, 218], 81), rough: 0.5, metal: 0.15, noShadow: true },
  steel_dark: { make: () => steel([42, 44, 48], 82), rough: 0.5, metal: 0.3 },
  truss: { make: truss, rough: 0.5, metal: 0.15, alphaTest: 0.5, twoSided: true, noShadow: true },
  floodlight: { make: floodlight, rough: 0.3, illum: 5, noShadow: true },
  videowall: { make: () => picture("videowall"), rough: 0.3, illum: 2.6, illumAll: true },
  facade: { make: facade, rough: 0.5, metal: 0.4, normal: 1 },
  facade_base: { make: facadeBase, rough: 0.2, metal: 0.2, illum: 1.2 },
  fin: { make: () => steel([226, 228, 232], 83), rough: 0.4, metal: 0.3 },
  pitch_dark: { make: () => grassImage(1024, bladeField(1024, () => Math.PI * 0.5, 130000, 11), () => GRASS_DARK, 311), rough: 1, normal: 1.1, noShadow: true },
  pitch_mid: { make: () => grassImage(1024, bladeField(1024, () => 0, 130000, 12), () => GRASS_MID, 312), rough: 1, normal: 1.1, noShadow: true },
  pitch_light: { make: () => grassImage(1024, bladeField(1024, () => Math.PI * 1.5, 130000, 13), () => GRASS_LIGHT, 313), rough: 1, normal: 1.1, noShadow: true },
  pitch_line: { make: pitchLine, rough: 1, noShadow: true },
  light_ring: { make: ringLeds, rough: 0.7, illum: 6, illumAll: true, twoSided: true, noShadow: true },
  pitch_grass: { make: pitchGrass, rough: 0.92, normal: 1.1, pitch: true },
  glass_rail: { make: glassRail, rough: 0.08, translucent: true, twoSided: true, noShadow: true },
  shutter: { make: shutter, rough: 0.6, metal: 0.3, normal: 1 },
  tunnel_trim: { make: () => picture("tunnel_trim"), rough: 0.5 },
};

const f3 = (v) => v.toFixed(6);
function vmat(name, m, has) {
  if (m.pitch) return `"Layer0"
{
	"shader"	"csgo_lightmappedgeneric.vfx"
	"F_LAYERS"	"0"
	"F_TEXTURETRANSFORMS"	"0"
	"g_vLayer1Tint"	"[1.000000 1.000000 1.000000 0.000000]"
	"TextureLayer1Color"	"${DIR}/${name}_color.png"
	"TextureLayer1AmbientOcclusion"	"materials/default/default_ao.tga"
	"TextureLayer1Normal"	"${DIR}/${name}_normal.png"
	"TextureLayer1Roughness"	"[${f3(m.rough)} ${f3(m.rough)} ${f3(m.rough)} 0.000000]"
	"SystemAttributes"
	{
		"PhysicsSurfaceProperties"	"Grass"
	}
}
`;
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
  return `"Layer0"\n{\n${lines.join("\n")}\n}\n`;
}

// The goal net: the knotted rope net v8 shows (tools/net/generate-goal-net.mjs, Feature Package
// material materials/soccer/goalnetting). The arena carries its own copy, so the net looks the
// same with or without the Feature Package mounted. Same map UVs as v8 (2.56 units per repeat),
// hence the same texture scale.
const NET_SRC = args["net-src"] ?? "E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/content/csgo_addons/soccermod_menu/materials/soccer";
if (args.addon && (!only || only.includes("goal_net"))) {
  const dir = path.join(args.addon, DIR);
  fs.mkdirSync(dir, { recursive: true });
  for (const part of ["color", "trans", "normal", "rough"]) fs.copyFileSync(path.join(NET_SRC, `goalnetting_rope_${part}.png`), path.join(dir, `goal_net_${part}.png`));
  fs.writeFileSync(path.join(dir, "goal_net.vmat"), `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"F_TRANSLUCENT"	"1"
	"g_flMetalness"	"0.000"
	"g_vTexCoordScale"	"[0.160 0.160]"
	"TextureColor"	"${DIR}/goal_net_color.png"
	"TextureTranslucency"	"${DIR}/goal_net_trans.png"
	"TextureNormal"	"${DIR}/goal_net_normal.png"
	"TextureRoughness"	"${DIR}/goal_net_rough.png"
	"SystemAttributes"
	{
		"PhysicsSurfaceProperties"	"glass"
	}
}
`);
  console.log("goal_net: v8's rope net (copied from the Feature Package source)");
}
const outDirs = [args.addon && path.join(args.addon, DIR), args.preview && path.join(args.preview, "tex")].filter(Boolean);
for (const d of outDirs) fs.mkdirSync(d, { recursive: true });
const previewFile = args.preview && path.join(args.preview, "materials.json");
const preview = previewFile && fs.existsSync(previewFile) && only ? JSON.parse(fs.readFileSync(previewFile, "utf8")) : {};
for (const [name, m] of Object.entries(MATERIALS)) {
  if (only && !only.includes(name)) continue;
  const r = m.make(), has = { normal: !!(m.normal && r.height), alpha: !!r.alpha, illum: !!r.illum };
  const normal = has.normal ? r.color.setHeight(r.height).normalMap(m.normal) : null;
  for (const d of outDirs) {
    r.color.png(path.join(d, `${name}_color.png`), 3);
    if (normal) normal.png(path.join(d, `${name}_normal.png`), 3);
    if (r.alpha) r.alpha.png(path.join(d, `${name}_trans.png`), 1);
    if (r.illum) r.illum.png(path.join(d, `${name}_illum.png`), 1);
  }
  if (args.addon) fs.writeFileSync(path.join(args.addon, DIR, `${name}.vmat`), vmat(name, m, has));
  preview[name] = { map: `${name}_color.png`, normal: normal ? `${name}_normal.png` : undefined, alpha: r.alpha ? `${name}_trans.png` : undefined,
    alphaTest: m.alphaTest, translucent: m.translucent, emissive: m.illum ? Math.min(1.6, m.illum / 3) : undefined, emissiveMask: r.illum ? `${name}_illum.png` : undefined,
    roughness: m.rough, metalness: m.metal ?? 0, twoSided: m.twoSided, noShadow: m.noShadow };
  console.log(`${name}: ${r.color.w}x${r.color.h}${normal ? " +normal" : ""}${r.alpha ? " +alpha" : ""}${r.illum ? " +illum" : ""}`);
}
if (previewFile) fs.writeFileSync(previewFile, JSON.stringify(preview, null, 1));
