#!/usr/bin/env node
// Brand boards (owner 2026-10-02: real brands mixed into the adverts; see tools/brands/README.md).
// The maps stay as they are; these are models the plugin puts
// in front of the maps' own boards (world coordinates, spawned at the origin):
//   models/soccermod_brands/hall_boards.vmdl   soccer_indoor_hall: LED adverts along the boards
//   models/soccermod_brands/2v2_boards.vmdl    soccer_2v2_arena: printed adverts on the kick boards
//   models/soccermod_brands/2v2_banners.vmdl   soccer_2v2_arena: two cloth banners on the east wall
// The pictures are the left half of each board page (tools/atmo/render-board-pages.ps1 and
// tools/brands/render-brand-pages.ps1: 1280 x 200, the unit twice).
//
//   node tools/brands/generate-brand-overlays.mjs <addon content dir> <pages dir>
import fs from "node:fs";
import path from "node:path";
import { Img, readPng, makeNoise, smooth, gridLine } from "../arena/lib/img.mjs";
import { staticDmx, staticVmdl } from "../arena/lib/dmx.mjs";
import { Scene } from "../arena/lib/mesh.mjs";
import * as HALL from "../hall/layout.mjs";
import * as GYM from "../gym/layout.mjs";

const [out, pagesDir, hallPreview, gymPreview] = process.argv.slice(2);   // the last two: preview scene folders of the local viewer (optional)
if (!out || !pagesDir) { console.error("usage: generate-brand-overlays.mjs <addon content dir> <pages dir>"); process.exit(1); }
const MATS = "materials/soccermod_brands", MODELS = "models/soccermod_brands";
fs.mkdirSync(path.join(out, MATS), { recursive: true }); fs.mkdirSync(path.join(out, MODELS), { recursive: true });

// ---- pictures -------------------------------------------------------------------------------------
const pages = new Map();
const page = (name) => { if (!pages.has(name)) pages.set(name, readPng(path.join(pagesDir, `${name}.png`))); return pages.get(name); };
// the unit of a page (its left half), fitted by height into w x h; the page's edge colour fills the rest
const unit = (name, w, h) => {
  const src = page(name), half = src.w / 2, scale = h / src.h, used = half * scale, lead = (w - used) / 2;
  return new Img(w, h).fill((x, y) => src.get(Math.max(0, Math.min(half - 1, Math.floor((x - lead) / scale))), Math.min(src.h - 1, Math.floor(y / scale))));
};
const vmat = (name, { illum, rough = 0.6, twoSided = false }) => `"Layer0"
{
\t"shader"\t"csgo_complex.vfx"
${illum ? '\t"F_SELF_ILLUM"\t"1"\n' : ""}${twoSided ? '\t"F_RENDER_BACKFACES"\t"1"\n' : ""}\t"F_DO_NOT_CAST_SHADOWS"\t"1"
\t"g_flMetalness"\t"0.000"
${illum ? `\t"g_flSelfIllumAlbedoFactor"\t"1.000"\n\t"g_flSelfIllumBrightness"\t"${illum.toFixed(3)}"\n\t"g_flSelfIllumScale"\t"1.000"\n\t"g_vSelfIllumTint"\t"[1.000000 1.000000 1.000000 0.000000]"\n\t"TextureSelfIllumMask"\t"[1.000000 1.000000 1.000000 0.000000]"\n` : ""}\t"TextureColor"\t"${MATS}/${name}_color.png"
\t"TextureRoughness"\t"[${rough.toFixed(6)} ${rough.toFixed(6)} ${rough.toFixed(6)} 0.000000]"
}
`;
const made = new Set();
function material(name, make, opts) {
  if (!made.has(name)) { make().png(path.join(out, MATS, `${name}_color.png`), 3); fs.writeFileSync(path.join(out, MATS, `${name}.vmat`), vmat(name, opts)); made.add(name); }
  return `${MATS}/${name}.vmat`;
}
// LED advert, as on the hall's boards: round dots on a dark panel (4 px per LED), 4 : 1
const led = (brand) => material(`led_${brand}`, () => {
  const src = unit(brand, 512, 128), dot = (x, y) => smooth(1.9, 1.1, Math.hypot((x % 4) - 1.5, (y % 4) - 1.5));
  return new Img(512, 128).fill((x, y) => { const c = src.get(x - (x % 4) + 1, y - (y % 4) + 1), d = 0.2 + 0.8 * dot(x, y); return [c[0] * d, c[1] * d, c[2] * d]; });
}, { illum: 2.2, rough: 0.35 });
// printed board (3 : 1 panel of the 2v2 kick boards): the advert with a dark frame and a few scuffs
const printed = (brand) => material(`print_${brand}`, () => {
  const src = unit(brand, 600, 200), n = makeNoise(900 + brand.length * 7);
  return new Img(600, 200).fill((x, y, u, v) => {
    if (x < 5 || x > 594 || y < 5 || y > 194) return [22, 24, 28];
    const c = src.get(x, y), scuff = Math.max(0, n.fbm(u, v, 12, 3, 0.3) - 0.62) * 1.2 * smooth(0.2, 0.7, v), k = 0.94 + (n.fbm(u, v, 5, 3) - 0.5) * 0.08;
    return [c[0] * k + (150 - c[0] * k) * scuff, c[1] * k + (150 - c[1] * k) * scuff, c[2] * k + (150 - c[2] * k) * scuff];
  });
}, { rough: 0.55 });
// cloth banner (3.2 : 1): soft folds, a hem, eyelets in the corners
const cloth = (brand) => material(`cloth_${brand}`, () => {
  const src = unit(brand, 640, 200), n = makeNoise(950 + brand.length * 11);
  return new Img(640, 200).fill((x, y, u, v) => {
    const c = src.get(x, y), fold = Math.sin(u * 23 + Math.sin(v * 5) * 0.8) * 0.05 + (n.fbm(u, v, 4, 3) - 0.5) * 0.12, hem = x < 7 || x > 632 || y < 7 || y > 192 ? 0.86 : 1;
    const eye = Math.hypot(Math.min(x, 639 - x) - 14, Math.min(y, 199 - y) - 14);
    if (eye < 5) return [70, 72, 76];
    const k = (0.95 + fold) * hem;
    return [c[0] * k, c[1] * k, c[2] * k];
  });
}, { rough: 0.9, twoSided: true });
void gridLine;

// the faces join a preview scene of the local viewer (as the crowd generators do)
function addToPreview(dir, faces) {
  const sceneFile = path.join(dir, "scene.json"), binFile = path.join(dir, "scene.bin"), matFile = path.join(dir, "materials.json");
  const manifest = JSON.parse(fs.readFileSync(sceneFile, "utf8")), mats = JSON.parse(fs.readFileSync(matFile, "utf8")), groups = new Map();
  for (const face of faces) {
    const key = path.basename(face.material, ".vmat"); if (!groups.has(key)) groups.set(key, []);
    for (let k = 1; k + 1 < face.pts.length; k++) for (const v of [0, k, k + 1]) groups.get(key).push(...face.pts[v], ...face.n, ...face.uvs[v]);
  }
  let bin = fs.readFileSync(binFile);
  for (const [key, data] of groups) {
    if (manifest.materials.some((m) => m.name === key)) continue;
    manifest.materials.push({ name: key, offset: bin.length, vertices: data.length / 8, twoSided: true });
    bin = Buffer.concat([bin, Buffer.from(new Float32Array(data).buffer)]);
    fs.copyFileSync(path.join(out, MATS, key + "_color.png"), path.join(dir, "tex", key + "_color.png"));
    mats[key] = { map: key + "_color.png", roughness: 0.6, metalness: 0, twoSided: true, emissive: key.startsWith("led_") ? 0.7 : undefined };
  }
  fs.writeFileSync(binFile, bin); fs.writeFileSync(sceneFile, JSON.stringify(manifest)); fs.writeFileSync(matFile, JSON.stringify(mats, null, 1));
}

// ---- models ---------------------------------------------------------------------------------------
// a picture: corners bottom-left, bottom-right, top-right, top-left as the viewer sees it; facing = towards the viewer
const picture = (scene, mat, bl, br, tr, tl, facing, [u0, u1] = [0, 1]) => scene.poly(mat, [bl, br, tr, tl], [[u0, 1], [u1, 1], [u1, 0], [u0, 0]], facing);
function writeModel(name, scene, previewDir) {
  const faces = scene.faces;
  if (previewDir) addToPreview(previewDir, faces);
  fs.writeFileSync(path.join(out, MODELS, `${name}.dmx`), staticDmx(name, faces));
  fs.writeFileSync(path.join(out, MODELS, `${name}.vmdl`), staticVmdl(MODELS, name, false));
  console.log(`${MODELS}/${name}: ${faces.length} faces, ${new Set(faces.map((f) => f.material)).size} materials`);
}

// the hall: a band 0.6 in front of the boards' LED face. Each stretch of board between the two goals is
// filled with whole adverts (about 176 units = 4 : 1 each), so none is cut at a goal or at the line's start.
{
  const order = ["cocacola", "voltwave", "nike", "fairplay", "adidas", "pitchline", "puma", "respect", "pepsi", "kickfuel", "emirates", "goalcrest"];
  const Z = HALL.Z, H = HALL.BOARD.h, L0 = HALL.boardLine(0), Ld = HALL.boardLine(-0.6), N = L0.pts.length, scene = new Scene();
  // runs of consecutive board segments, starting right after a goal
  const first = [...Array(N).keys()].find((i) => !L0.pts[i].goal && L0.pts[(i + N - 1) % N].goal), runs = [];
  for (let k = 0, run = null; k < N; k++) {
    const i = (first + k) % N;
    if (L0.pts[i].goal) { run = null; continue; }
    if (!run) runs.push(run = []);
    run.push(i);
  }
  let advert = 0;
  for (const run of runs) {
    const length = run.reduce((sum, i) => sum + L0.pts[i].len, 0), count = Math.max(1, Math.round(length / (H * 4))), slot = length / count;
    let r0 = 0;
    for (const i of run) {
      const a0 = L0.pts[i], a = Ld.pts[i], b = Ld.pts[(i + 1) % N], r1 = r0 + a0.len;
      // cut the segment at the advert borders
      for (let k = Math.floor(r0 / slot + 1e-9); k < count && k * slot < r1 - 1e-6; k++) {
        const c0 = Math.max(r0, k * slot), c1 = Math.min(r1, (k + 1) * slot); if (c1 - c0 < 1e-4) continue;
        const at = (d) => { const t = (d - r0) / a0.len; return [a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t]; }, p = at(c0), q = at(c1);
        // seen from the court the board line runs right to left: u falls along it
        const u0 = 1 - (c0 - k * slot) / slot, u1 = 1 - (c1 - k * slot) / slot;
        picture(scene, led(order[(advert + k) % order.length]), [q[0], q[1], Z(0.4)], [p[0], p[1], Z(0.4)], [p[0], p[1], Z(H)], [q[0], q[1], Z(H)], [-a0.nx, -a0.ny, 0], [u1, u0]);
      }
      r0 = r1;
    }
    advert += count;
    console.log(`hall run: ${length.toFixed(0)} units, ${count} adverts of ${slot.toFixed(1)}`);
  }
  writeModel("hall_boards", scene, hallPreview);
}

// the 2v2 hall: one printed advert per kick-board panel (120 x 40), on the court side
{
  const order = ["soccermod", "cocacola", "nike", "adidas", "kickfuel", "puma", "pepsi", "emirates", "voltwave", "cocacola", "adidas", "nike"];
  const Z = GYM.Z, { hx, hy } = GYM.COURT, H = GYM.BOARD.h, BAY = GYM.MESH.bay, scene = new Scene();
  for (const s of [1, -1]) for (let k = 0; k * BAY < 2 * hy - 1; k++) {
    const y0 = -hy + k * BAY, y1 = y0 + BAY, x = s * (hx - 0.5), mat = printed(order[(k + (s > 0 ? 0 : 5)) % order.length]);
    // east side (s > 0): the viewer looks along +x, left is +y; west side: left is -y
    const [ya, yb] = s > 0 ? [y1, y0] : [y0, y1];
    picture(scene, mat, [x, ya, Z(0.4)], [x, yb, Z(0.4)], [x, yb, Z(H - 0.2)], [x, ya, Z(H - 0.2)], [-s, 0, 0]);
  }
  writeModel("2v2_boards", scene, gymPreview);
}

// the 2v2 hall: two cloth banners on the east wall, left and right of the mural (320 x 100)
{
  const Z = GYM.Z, x = GYM.HALL.x - 2.5, scene = new Scene();
  for (const [brand, cy] of [["cocacola", 540], ["adidas", -540]]) {
    // the viewer looks along +x: left is +y
    const ya = cy + 160, yb = cy - 160, h0 = 172, h1 = 272;
    picture(scene, cloth(brand), [x, ya, Z(h0)], [x, yb, Z(h0)], [x, yb, Z(h1)], [x, ya, Z(h1)], [-1, 0, 0]);
  }
  writeModel("2v2_banners", scene, gymPreview);
}
