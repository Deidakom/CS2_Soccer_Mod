#!/usr/bin/env node
// SoccerMod street arena: the radar picture (drawn from the layout, top down) and the overview file.
// Frame: 1024 px over 2560 units, centred on the centre spot (2.5 units per pixel).
//
//   node tools/street/generate-street-radar.mjs <content addon dir>
import fs from "node:fs";
import path from "node:path";
import { Img } from "../arena/lib/img.mjs";
import { MAP_NAME, COURT, WEST_WALL, EAST_WALL, GOAL, WEST, EAST, EL, NORTH, SOUTH, courtLines } from "./layout.mjs";

const addon = process.argv[2];
if (!addon) { console.error("usage: generate-street-radar.mjs <content addon dir>"); process.exit(1); }
const N = 1024, ORIGIN = 1280, SCALE = (2 * ORIGIN) / N;
const inside = (poly, x, y) => { let c = false; for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) { const a = poly[i], b = poly[j]; if ((a[1] > y) !== (b[1] > y) && x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]) c = !c; } return c; };
const lines = courtLines(), { hx, hy } = COURT, by = hy - 16;
const img = new Img(N, N).fill((i, j) => {
  const x = -ORIGIN + (i + 0.5) * SCALE, y = ORIGIN - (j + 0.5) * SCALE, ax = Math.abs(x), ay = Math.abs(y);
  if (ax <= hx && ay <= hy) {
    for (const poly of lines) if (inside(poly, x, y)) return [236, 234, 226, 255];
    if (Math.hypot(x, y) < 136) return [44, 46, 52, 255];
    for (const s of [1, -1]) if (Math.hypot(x, y - s * by) < 247 && s * y < by) return s > 0 ? [214, 96, 76, 255] : [52, 150, 156, 255];
    return [112, 108, 104, 255];
  }
  // the two containers, the walls of the cage
  for (const s of [1, -1]) if (ax <= GOAL.housingHalf && s * y > hy && s * y <= hy + GOAL.housingDepth) return ax <= GOAL.half ? [40, 40, 46, 255] : s > 0 ? [170, 44, 38, 255] : [38, 82, 150, 255];
  if (ax <= hx + EAST_WALL.t && ay <= hy + 14) return [200, 200, 196, 255];
  // west: sidewalk, street, shops; east: the raised street under the line, tenements; the ends
  if (x < 0 && ay <= WEST.y) { if (x > WEST.walk1) return [176, 172, 160, 255]; if (x > WEST.street1) return [62, 62, 66, 255]; if (x > WEST.far1) return [150, 148, 142, 255]; if (x > WEST.back) return [120, 86, 70, 255]; }
  if (x > 0 && ay <= EL.y) { if (x > EL.cx - EL.width / 2 && x < EL.cx + EL.width / 2) return [54, 78, 64, 255]; if (x < EAST.facade) return [86, 84, 82, 255]; if (x < EAST.back && ay <= EAST.y) return [120, 76, 60, 255]; }
  if (y > NORTH.yard && y < NORTH.facade && ax < 900) return [74, 72, 70, 255];
  if (y >= NORTH.facade && y < NORTH.back && x > -900 && x < 560) return [112, 70, 58, 255];
  if (y < SOUTH.yard && y > SOUTH.tiers[0].y && ax < 1000) return [74, 72, 70, 255];
  if (y <= SOUTH.tiers[0].y && y > SOUTH.back && x > -1000 && x < 560) return Math.abs(x) < SOUTH.stairHalf ? [214, 60, 50, 255] : [[232, 190, 70], [60, 170, 170], [226, 124, 140], [96, 168, 96]][(Math.floor((x + 1000) / 150) + Math.floor((y - SOUTH.back) / 200)) % 4].concat(255);
  return [0, 0, 0, 0];
});
void WEST_WALL;
const dir = path.join(addon, "panorama/images/overheadmaps"), ov = path.join(addon, "resource/overviews");
fs.mkdirSync(dir, { recursive: true }); fs.mkdirSync(ov, { recursive: true });
img.png(path.join(dir, `${MAP_NAME}_radar.png`), 4);
fs.writeFileSync(path.join(dir, `${MAP_NAME}_radar_psd.vtex`), `<!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
"CDmeVtex"
{
    "m_inputTextureArray" "element_array"
    [
        "CDmeInputTexture"
        {
            "m_name" "string" "RadarTexture"
            "m_fileName" "string" "panorama/images/overheadmaps/${MAP_NAME}_radar.png"
            "m_colorSpace" "string" "srgb"
            "m_typeString" "string" "2D"
            "m_imageProcessorArray" "element_array"
            [
            ]
        }
    ]
    "m_outputTypeString" "string" "2D"
    "m_outputFormat" "string" "BGRA8888"
    "m_outputClearColor" "vector4" "0 0 0 0"
    "m_nOutputMinDimension" "int" "0"
    "m_nOutputMaxDimension" "int" "1024"
    "m_textureOutputChannelArray" "element_array"
    [
        "CDmeTextureOutputChannel"
        {
            "m_inputTextureArray" "string_array"
            [
                "RadarTexture"
            ]
            "m_srcChannels" "string" "rgba"
            "m_dstChannels" "string" "rgba"
            "m_mipAlgorithm" "CDmeImageProcessor"
            {
                "m_algorithm" "string" ""
                "m_stringArg" "string" ""
                "m_vFloat4Arg" "vector4" "0 0 0 0"
            }
            "m_outputColorSpace" "string" "srgb"
        }
    ]
    "m_vClamp" "vector3" "0 0 0"
    "m_bNoLod" "bool" "1"
}
`);
fs.writeFileSync(path.join(ov, `${MAP_NAME}.txt`), `"${MAP_NAME}"
{
    "material"      "overviews/${MAP_NAME}"
    "pos_x"         "${-ORIGIN}"
    "pos_y"         "${ORIGIN}"
    "scale"         "${SCALE}"
    "rotate"        "0"
    "zoom"          "1.0"
    "inset_left"    "0.0"
    "inset_top"     "0.0"
    "inset_right"   "0.0"
    "inset_bottom"  "0.0"
}
`);
console.log(`radar ${N}x${N}: ${path.join(dir, `${MAP_NAME}_radar.png`)}`);
