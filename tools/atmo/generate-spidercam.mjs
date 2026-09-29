#!/usr/bin/env node
// "Arena Vision" spidercam pod (v8 only): a TV camera pod hanging on 4 cables
// (the cables are CBeams from the plugin). Box body + lens + gimbal plate,
// dark grey, a red self-lit tally light. Faces +x, origin at the cable mount
// (top centre), so the plugin can aim it at the ball with yaw/pitch.
//
// usage: node tools/atmo/generate-spidercam.mjs <addon content dir>
import fs from "node:fs";
import path from "node:path";
import { meshDmx, vmdlText } from "./dmx-lib.mjs";

const out = process.argv[2];
if (!out) { console.error("usage: generate-spidercam.mjs <addon content dir>"); process.exit(1); }
const SOURCE = "tools/atmo/generate-spidercam.mjs";
const MODEL = "models/soccermod/atmo/spidercam";
const MAT_BODY = "materials/soccermod/atmo/spidercam_body.vmat";
const MAT_TALLY = "materials/soccermod/atmo/spidercam_tally.vmat";

function mesh(name, material, boxes) {
  const m = { name, material, positions: [], normals: [], uvs: [], weights: [], indices: [], faces: [] };
  for (const [x0, y0, z0, x1, y1, z1] of boxes) {
    const faces = [
      [[1, 0, 0], [[x1, y0, z0], [x1, y1, z0], [x1, y1, z1], [x1, y0, z1]]],
      [[-1, 0, 0], [[x0, y1, z0], [x0, y0, z0], [x0, y0, z1], [x0, y1, z1]]],
      [[0, 1, 0], [[x1, y1, z0], [x0, y1, z0], [x0, y1, z1], [x1, y1, z1]]],
      [[0, -1, 0], [[x0, y0, z0], [x1, y0, z0], [x1, y0, z1], [x0, y0, z1]]],
      [[0, 0, 1], [[x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]]],
      [[0, 0, -1], [[x0, y1, z0], [x1, y1, z0], [x1, y0, z0], [x0, y0, z0]]],
    ];
    for (const [n, quad] of faces) {
      const b = m.positions.length;
      quad.forEach((p, i) => { m.positions.push(p); m.normals.push(n); m.uvs.push([[0, 0], [1, 0], [1, 1], [0, 1]][i]); m.weights.push([1, 0]); m.indices.push([0, 0]); });
      m.faces.push([b, b + 1, b + 2, b + 3]);
    }
  }
  return m;
}
const bones = [{ name: "root", pos: [0, 0, 0] }];
const body = mesh("spidercam", MAT_BODY, [
  [-6, -14, -4, 6, 14, 0],        // cable mount plate
  [-2, -2, -14, 2, 2, -4],        // gimbal post
  [-18, -13, -36, 18, 13, -14],   // camera body
  [18, -8, -32, 34, 8, -18],      // lens
  [-24, -10, -32, -18, 10, -18],  // back unit
]);
const tally = mesh("spidercam_tally", MAT_TALLY, [[8, -3, -14, 14, 3, -11]]);

const write = (rel, data) => { const p = path.join(out, rel); fs.mkdirSync(path.dirname(p), { recursive: true }); fs.writeFileSync(p, data); };
write(MAT_BODY, `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_RENDER_BACKFACES"\t"1"
\t"g_flMetalness"\t"0.600"
\t"TextureColor"\t"[0.090000 0.095000 0.105000 0.000000]"
\t"TextureRoughness"\t"[0.350000 0.350000 0.350000 0.000000]"
}
`);
write(MAT_TALLY, `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
\t"F_SELF_ILLUM"\t"1"
\t"F_RENDER_BACKFACES"\t"1"
\t"g_flSelfIllumBrightness"\t"8.000"
\t"g_flSelfIllumAlbedoFactor"\t"1.000"
\t"TextureColor"\t"[1.000000 0.050000 0.030000 0.000000]"
}
`);
write(`${MODEL}.dmx`, meshDmx(SOURCE, bones, body));
write(`${MODEL}_tally.dmx`, meshDmx(SOURCE, bones, tally));
let vmdl = vmdlText(MODEL, "spidercam", [], []);
vmdl = vmdl.replace(`\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "spidercam"\n\t\t\t\t\t\tfilename = "${MODEL}.dmx"\n\t\t\t\t\t},`,
  `\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "spidercam"\n\t\t\t\t\t\tfilename = "${MODEL}.dmx"\n\t\t\t\t\t},\n\t\t\t\t\t{\n\t\t\t\t\t\t_class = "RenderMeshFile"\n\t\t\t\t\t\tname = "spidercam_tally"\n\t\t\t\t\t\tfilename = "${MODEL}_tally.dmx"\n\t\t\t\t\t},`);
vmdl = vmdl.replace(/\t\t\t\{\n\t\t\t\t_class = "AnimationList"[\s\S]*?\n\t\t\t\},\n/, "");
write(`${MODEL}.vmdl`, vmdl);
console.log(`${MODEL}: ${body.positions.length + tally.positions.length} vertices`);
