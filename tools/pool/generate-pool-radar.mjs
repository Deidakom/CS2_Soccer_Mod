#!/usr/bin/env node
// SoccerMod 1v1 cage: the radar picture (drawn from the layout, top down) and the overview file.
// Frame: 1024 px over 1792 units, centred on the centre spot (1.75 units per pixel).
//
//   node tools/pool/generate-pool-radar.mjs <content addon dir>
import fs from "node:fs";
import path from "node:path";
import { Img } from "../arena/lib/img.mjs";
import { MAP_NAME, POOL, GOAL, HALL, ARCADE, lanes, pitchLines } from "./layout.mjs";

const addon = process.argv[2];
if (!addon) { console.error("usage: generate-pool-radar.mjs <content addon dir>"); process.exit(1); }
const N = 1024, ORIGIN = 896, SCALE = (2 * ORIGIN) / N;
const inside = (poly, x, y) => { let c = false; for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) { const a = poly[i], b = poly[j]; if ((a[1] > y) !== (b[1] > y) && x < (b[0] - a[0]) * (y - a[1]) / (b[1] - a[1]) + a[0]) c = !c; } return c; };
const lines = pitchLines(), strips = lanes(), mouth = GOAL.half + GOAL.post;
const img = new Img(N, N).fill((i, j) => {
  const x = -ORIGIN + (i + 0.5) * SCALE, y = ORIGIN - (j + 0.5) * SCALE, ax = Math.abs(x), ay = Math.abs(y);
  if (ax > HALL.x || ay > HALL.y) return [0, 0, 0, 0];
  if (ax <= POOL.hx && ay <= POOL.hy) {                                                      // the pool's bottom = the pitch
    for (const poly of lines) if (inside(poly, x, y)) return [240, 240, 236, 255];
    for (const poly of strips) if (inside(poly, x, y)) return [30, 36, 44, 255];
    return (Math.floor((x + POOL.hx) / 24) + Math.floor((y + POOL.hy) / 24)) % 2 ? [172, 214, 212, 255] : [164, 206, 206, 255];
  }
  if (ax <= mouth && ay <= POOL.hy + GOAL.depth) return [24, 40, 70, 255];                      // the goal niches
  if (ax <= POOL.hx + 6 && ay <= POOL.hy + 6) return [60, 66, 70, 255];                         // the cage on the edge
  if (ax > ARCADE.x) return [112, 96, 74, 255];                                                // cabins / gallery
  return (Math.floor(ax / 48) + Math.floor(ay / 48)) % 2 ? [190, 180, 156, 255] : [182, 172, 148, 255];   // the deck
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
