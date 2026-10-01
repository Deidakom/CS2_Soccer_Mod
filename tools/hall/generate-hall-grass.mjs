#!/usr/bin/env node
// SoccerMod indoor hall: the "3D grass" for the hall's court (the plugin's per-player 3D grass,
// Grass.cs). Same shell technique as the stadium's (tools/grass/generate-shell-grass.mjs): 9 stacked
// cut-out layers, 0.25 apart, with a turf texture made of small blade dots; white layers of the
// same kind over the painted lines. The court is not v8's, so it has its own tiles:
//   models/soccermod_hall/grass_<tx>_<ty>.vmdl   12 x 16 tiles of 140 x 143.75, each centred on its tile
//   materials/soccermod_hall/grass_green.vmat, grass_white.vmat (the stadium's blade textures, the
//   green one toned to the hall's turf)
// The plugin spawns tile (tx, ty) at its centre on the floor. No collision.
//
// usage: node tools/hall/generate-hall-grass.mjs <content addon dir> [--src <soccermod_menu content dir>]
import fs from "node:fs";
import path from "node:path";
import { Scene } from "../arena/lib/mesh.mjs";
import { staticDmx, staticVmdl } from "../arena/lib/dmx.mjs";
import { Img, readPng } from "../arena/lib/img.mjs";
import { PITCH, boardLine, pitchLines, clipConvex } from "./layout.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-hall-grass.mjs <addon content dir> [--src <dir>]"); process.exit(1); }
const srcArg = process.argv.indexOf("--src");
const SRC = srcArg > 0 ? process.argv[srcArg + 1] : "E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/content/csgo_addons/soccermod_menu";
export const GRASS = { tilesX: 12, tilesY: 16, layers: 9, start: 0.25, spacing: 0.25, uvTile: 128 };
const MODEL_DIR = "models/soccermod_hall", MAT_DIR = "materials/soccermod_hall";
const GREEN = `${MAT_DIR}/grass_green.vmat`, WHITE = `${MAT_DIR}/grass_white.vmat`, UP = [0, 0, 1];

// ---- materials: the stadium's blade textures; the green one toned to the hall's turf -----------------
{
  const dir = path.join(out, MAT_DIR), from = path.join(SRC, "materials/soccermod");
  fs.mkdirSync(dir, { recursive: true });
  const green = readPng(path.join(from, "grass_shell_green_color.png")), toned = new Img(green.w, green.h);
  toned.fill((x, y) => { const c = green.get(x, y); return [c[0] * 0.97, c[1] * 1.16, c[2] * 1.38]; });
  toned.png(path.join(dir, "grass_green_color.png"), 3);
  fs.copyFileSync(path.join(from, "grass_shell_white_color.png"), path.join(dir, "grass_white_color.png"));
  fs.copyFileSync(path.join(from, "grass_shell_trans.png"), path.join(dir, "grass_trans.png"));
  const vmat = (color) => `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"F_ALPHA_TEST"	"1"
	"g_flAlphaTestReference"	"0.400"
	"F_RENDER_BACKFACES"	"1"
	"F_DO_NOT_CAST_SHADOWS"	"1"
	"TextureColor"	"${MAT_DIR}/${color}_color.png"
	"TextureTranslucency"	"${MAT_DIR}/grass_trans.png"
	"TextureRoughness"	"[0.900000 0.900000 0.900000 0.000000]"
	"SystemAttributes"
	{
		"PhysicsSurfaceProperties"	"Grass"
	}
}
`;
  fs.writeFileSync(path.join(dir, "grass_green.vmat"), vmat("grass_green"));
  fs.writeFileSync(path.join(dir, "grass_white.vmat"), vmat("grass_white"));
}

// ---- tiles ------------------------------------------------------------------------------------------------
const { hx, gy } = PITCH, tw = (2 * hx) / GRASS.tilesX, th = (2 * gy) / GRASS.tilesY;
const outline = boardLine(0).pts.map((p) => [p.x, p.y]), lines = pitchLines();
const dir = path.join(out, MODEL_DIR);
fs.mkdirSync(dir, { recursive: true });
let faces = 0, empty = 0;
for (let ty = 0; ty < GRASS.tilesY; ty++) for (let tx = 0; tx < GRASS.tilesX; tx++) {
  const x0 = -hx + tx * tw, y0 = -gy + ty * th, cx = x0 + tw / 2, cy = y0 + th / 2, rect = [[x0, y0], [x0 + tw, y0], [x0 + tw, y0 + th], [x0, y0 + th]];
  const green = clipConvex(rect, outline), white = lines.map((l) => clipConvex(l, rect)).filter((p) => p.length >= 3);
  const scene = new Scene();
  const put = (material, poly, z) => {
    // as triangles from the first corner; coordinates relative to the tile centre, uv from the court
    for (let i = 1; i + 1 < poly.length; i++) { const tri = [poly[0], poly[i], poly[i + 1]]; scene.poly(material, tri.map(([x, y]) => [x - cx, y - cy, z]), tri.map(([x, y]) => [x / GRASS.uvTile, -y / GRASS.uvTile]), UP); }
  };
  for (let l = 0; l < GRASS.layers; l++) {
    const z = GRASS.start + l * GRASS.spacing;
    if (green.length >= 3) put(GREEN, green, z);
    for (const w of white) put(WHITE, w, z + 0.01);
  }
  if (!scene.faces.length) { empty++; put(GREEN, [[cx - 0.5, cy - 0.5], [cx + 0.5, cy - 0.5], [cx, cy + 0.5]], -8); }   // a corner tile outside the rounded boards: a speck under the floor
  const name = `grass_${tx}_${ty}`;
  fs.writeFileSync(path.join(dir, `${name}.dmx`), staticDmx(name, scene.faces));
  fs.writeFileSync(path.join(dir, `${name}.vmdl`), staticVmdl(MODEL_DIR, name, false));
  faces += scene.faces.length;
}
console.log(`hall grass: ${GRASS.tilesX * GRASS.tilesY} tiles (${empty} empty), ${GRASS.layers} layers, ${faces} faces`);
