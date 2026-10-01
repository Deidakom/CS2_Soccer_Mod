#!/usr/bin/env node
// SoccerMod indoor hall: the radar picture (drawn from the layout, top down) and the overview file.
// Frame: 1024 px over 3600 units, centred on the centre spot (3.515625 units per pixel).
//
//   node tools/hall/generate-hall-radar.mjs <content addon dir>
import fs from "node:fs";
import path from "node:path";
import { Img } from "../arena/lib/img.mjs";
import { MAP_NAME, PITCH, BOARD, GOAL, HALL, WEST, EAST, END, WEST_TOP, EAST_TOP, END_TOP, boardLine } from "./layout.mjs";

const addon = process.argv[2];
if (!addon) { console.error("usage: generate-hall-radar.mjs <content addon dir>"); process.exit(1); }
const N = 1024, ORIGIN = 1800, SCALE = (2 * ORIGIN) / N;
const line0 = boardLine(0).pts, line8 = boardLine(BOARD.t).pts;
const inside = (pts, x, y) => { let c = false; for (let i = 0, j = pts.length - 1; i < pts.length; j = i++) { const a = pts[i], b = pts[j]; if ((a.y > y) !== (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x) c = !c; } return c; };
const near = (v, at, w) => Math.abs(v - at) <= w;
const img = new Img(N, N).fill((i, j) => {
  const x = -ORIGIN + (i + 0.5) * SCALE, y = ORIGIN - (j + 0.5) * SCALE, ax = Math.abs(x), ay = Math.abs(y);
  if (x < HALL.x0 || x > HALL.x1 || ay > HALL.y) return [0, 0, 0, 0];
  if (inside(line0, x, y)) {
    const band = Math.floor((y + PITCH.gy) / ((2 * PITCH.gy) / 20)) % 2 ? [66, 124, 52] : [58, 112, 46];
    const r = Math.hypot(x, y), W = 5;
    if (near(y, 0, W) || near(r, 190, W) || r < 10) return [240, 240, 236, 255];
    for (const s of [1, -1]) {
      const gy = s * PITCH.gy, d = s * (gy - y);                 // distance from this goal line
      if (d >= 0 && d < 310) { const rr = Math.hypot(ax - GOAL.half, d); if ((ax <= GOAL.half && near(d, 300, W)) || (ax > GOAL.half && near(rr, 300, W))) return [240, 240, 236, 255]; }
    }
    return [...band, 255];
  }
  if (inside(line8, x, y)) return [228, 230, 234, 255];                                              // boards
  for (const s of [1, -1]) if (ax <= GOAL.housingHalf && s * y >= PITCH.gy && s * y <= PITCH.gy + GOAL.housingDepth) return ax <= GOAL.half + 8 ? [60, 60, 66, 255] : [34, 36, 42, 255];
  if (x <= WEST.x && x >= WEST_TOP.x && ay <= WEST.y) return (Math.floor((WEST.x - x) / WEST.depth) % 2 ? [176, 44, 48, 255] : [196, 52, 56, 255]);
  if (x >= EAST.x && x <= EAST_TOP.x && ay <= EAST.y) return (Math.floor((x - EAST.x) / EAST.depth) % 2 ? [40, 74, 170, 255] : [48, 86, 190, 255]);
  if (ay >= END.y && ay <= END_TOP.y && ax <= END.x) return y > 0 ? [150, 40, 44, 255] : [40, 66, 150, 255];
  return [74, 76, 82, 255];                                                                            // walkways, concourses
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
