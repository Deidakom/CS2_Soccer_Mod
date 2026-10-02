#!/usr/bin/env node
// SoccerMod gym: the radar picture (drawn from the layout, top down) and the overview file.
// Frame: 1024 px over 1792 units, centred on the centre spot (1.75 units per pixel).
//
//   node tools/gym/generate-gym-radar.mjs <content addon dir>
import fs from "node:fs";
import path from "node:path";
import { Img } from "../arena/lib/img.mjs";
import { MAP_NAME, COURT, BOARD, GOAL, HALL, STAND, AREA, courtLines } from "./layout.mjs";

const addon = process.argv[2];
if (!addon) { console.error("usage: generate-gym-radar.mjs <content addon dir>"); process.exit(1); }
const N = 1024, ORIGIN = 896, SCALE = (2 * ORIGIN) / N;
const inside = (poly, x, y) => { let c = false; for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) { const a = poly[i], b = poly[j]; if ((a[1] > y) !== (b[1] > y) && x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]) c = !c; } return c; };
const lines = courtLines(), mouth = GOAL.half + GOAL.post;
const img = new Img(N, N).fill((i, j) => {
  const x = -ORIGIN + (i + 0.5) * SCALE, y = ORIGIN - (j + 0.5) * SCALE, ax = Math.abs(x), ay = Math.abs(y);
  if (ay > COURT.hy) return ax <= mouth && ay <= COURT.hy + GOAL.depth ? [40, 40, 46, 255] : [0, 0, 0, 0];
  if (ax > HALL.x) return [0, 0, 0, 0];
  if (ax <= COURT.hx) {
    for (const poly of lines.white) if (inside(poly, x, y)) return [240, 240, 236, 255];
    if (Math.hypot(x, y) < AREA.emblem) return [34, 38, 48, 255];
    for (const s of [1, -1]) if (Math.hypot(x, y - s * AREA.y) < AREA.r && s * y < AREA.y) return s > 0 ? [186, 44, 46, 255] : [40, 86, 186, 255];
    for (const poly of lines.black) if (inside(poly, x, y)) return [40, 42, 48, 255];
    return Math.floor((x + COURT.hx) / 16) % 2 ? [208, 168, 110, 255] : [200, 160, 104, 255];
  }
  if (ax <= COURT.hx + BOARD.t) return [30, 34, 42, 255];                                   // the cage
  return Math.floor((ax - STAND.x0) / STAND.depth) % 2 ? [92, 104, 118, 255] : [78, 90, 104, 255];   // the fans' steps
});
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
