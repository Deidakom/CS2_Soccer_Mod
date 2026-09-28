#!/usr/bin/env node
// Ball shadows (owner 2026-09-28: "the ball looks floating" on the floor and
// on the 3D grass). Two flat models, placed by the plugin (ContactShadow.cs):
//
//   models/soccermod/ball/contact_shadow.vmdl - soft dark disc straight under
//     the ball (the contact / ambient shadow a runtime prop never gets baked).
//     8 skins, skin 0 darkest .. skin 7 faintest, chosen by the ball's height.
//   models/soccermod/ball/sun_shadow.vmdl - the ball's sun shadow for the
//     unlit 3D grass (it receives no dynamic shadow): an ellipse stretched by
//     1 / sin(sun pitch) along local +x, the plugin turns it to the sun's yaw.
//
// Both lie in the xy plane at z = 0, centred on the origin, facing up.
// Material: csgo_complex translucent (as the kickoff curtain), black colour,
// the shadow shape in the translucency texture, no shadow casting.
//
// usage: node tools/ball/generate-ball-shadow.mjs <csgo_addons/soccermod_menu>
import fs from "node:fs";
import path from "node:path";
import zlib from "node:zlib";
import crypto from "node:crypto";

const out = process.argv[2];
if (!out) { console.error("usage: generate-ball-shadow.mjs <addon content dir>"); process.exit(1); }

const BALL_R = 18.805;                       // DefaultBallCollisionRadius (the visual matches it)
const CONTACT_R = 27;                        // disc radius: about 1.45 ball radii
const CONTACT_ALPHA = 0.62;                  // centre opacity of skin 0
const CONTACT_LEVELS = [1, 0.84, 0.68, 0.54, 0.41, 0.3, 0.2, 0.11];
const SUN_PITCH = 45;                        // soccer_cssl_stadium_v8 light_environment angles "45 45 0"
const SUN_A = BALL_R / Math.sin(SUN_PITCH * Math.PI / 180), SUN_B = BALL_R, SUN_SOFT = 7;
// Floor in the roof shadow = 1.510 / 2.356 of the sunlit floor (bake grass
// levels), so a sun shadow darkens the unlit grass by about 36 %.
const SUN_ALPHA = 0.36;
const MODEL_C = "models/soccermod/ball/contact_shadow", MODEL_S = "models/soccermod/ball/sun_shadow";
const MAT_DIR = "materials/soccermod/ball";

// ---- helpers (as tools/kickoff/generate-kickoff-curtain.mjs) ---------------------------
function png(w, h, pixel) {
  const raw = Buffer.alloc((w * 3 + 1) * h);
  for (let y = 0; y < h; y++) { raw[y * (w * 3 + 1)] = 0; for (let x = 0; x < w; x++) pixel(x, y, raw, y * (w * 3 + 1) + 1 + x * 3); }
  const table = Array.from({ length: 256 }, (_, n) => { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; return c >>> 0; });
  const crc = (b) => { let c = 0xffffffff; for (const v of b) c = table[(c ^ v) & 0xff] ^ (c >>> 8); return (c ^ 0xffffffff) >>> 0; };
  const chunk = (type, data) => { const len = Buffer.alloc(4); len.writeUInt32BE(data.length); const td = Buffer.concat([Buffer.from(type), data]); const c = Buffer.alloc(4); c.writeUInt32BE(crc(td)); return Buffer.concat([len, td, c]); };
  const ihdr = Buffer.alloc(13); ihdr.writeUInt32BE(w, 0); ihdr.writeUInt32BE(h, 4); ihdr[8] = 8; ihdr[9] = 2;
  return Buffer.concat([Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]), chunk("IHDR", ihdr), chunk("IDAT", zlib.deflateSync(raw, { level: 9 })), chunk("IEND", Buffer.alloc(0))]);
}
const clamp = (v) => Math.max(0, Math.min(255, Math.round(v)));

const id = () => crypto.randomUUID();
const q = (vals, ind) => vals.map((v) => `${ind}"${v}"`).join(",\n");
const f4 = (n) => Number(n.toFixed(4)).toString();
function dmxFor(name, positions, uvs, normals, faces, mtl) {
const idx = q(positions.map((_, i) => i), "\t\t");
const I = { model: id(), dag: id(), bind: id() };
return `<!-- dmx encoding keyvalues2 4 format model 22 -->
"DmElement"
{
\t"id" "elementid" "${id()}"
\t"name" "string" "root"
\t"skeleton" "element" "${I.model}"
\t"model" "element" "${I.model}"
\t"exportTags" "DmeExportTags"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "exportTags"
\t\t"source" "string" "tools/kickoff/generate-kickoff-curtain.mjs"
\t}
}

"DmeModel"
{
\t"id" "elementid" "${I.model}"
\t"name" "string" "${name}"
\t"transform" "DmeTransform"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"position" "vector3" "0 0 0"
\t\t"orientation" "quaternion" "0 0 0 1"
\t}
\t"shape" "element" ""
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t\t"element" "${I.dag}"
\t]
\t"jointList" "element_array"
\t[
\t\t"element" "${I.dag}"
\t]
\t"baseStates" "element_array"
\t[
\t\t"DmeTransformsList"
\t\t{
\t\t\t"id" "elementid" "${id()}"
\t\t\t"transforms" "element_array"
\t\t\t[
\t\t\t\t"DmeTransform"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"position" "vector3" "0 0 0"
\t\t\t\t\t"orientation" "quaternion" "0 0 0 1"
\t\t\t\t}
\t\t\t]
\t\t}
\t]
\t"axisSystem" "DmeAxisSystem"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"upAxis" "int" "3"
\t\t"forwardParity" "int" "1"
\t\t"coordSys" "int" "0"
\t}
}

"DmeDag"
{
\t"id" "elementid" "${I.dag}"
\t"name" "string" "${name}"
\t"transform" "DmeTransform"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"position" "vector3" "0 0 0"
\t\t"orientation" "quaternion" "0 0 0 1"
\t}
\t"shape" "DmeMesh"
\t{
\t\t"id" "elementid" "${id()}"
\t\t"name" "string" "${name}"
\t\t"bindState" "element" ""
\t\t"currentState" "element" "${I.bind}"
\t\t"baseStates" "element_array"
\t\t[
\t\t\t"element" "${I.bind}"
\t\t]
\t\t"deltaStates" "element_array"
\t\t[
\t\t]
\t\t"faceSets" "element_array"
\t\t[
\t\t\t"DmeFaceSet"
\t\t\t{
\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t"name" "string" "curtain"
\t\t\t\t"faces" "int_array"
\t\t\t\t[
${q(faces.flatMap((f) => [...f, -1]), "\t\t\t\t\t")}
\t\t\t\t]
\t\t\t\t"material" "DmeMaterial"
\t\t\t\t{
\t\t\t\t\t"id" "elementid" "${id()}"
\t\t\t\t\t"name" "string" "material"
\t\t\t\t\t"mtlName" "string" "${mtl}.vmat"
\t\t\t\t}
\t\t\t}
\t\t]
\t\t"deltaStateWeights" "vector2_array"
\t\t[
\t\t]
\t\t"deltaStateWeightsLagged" "vector2_array"
\t\t[
\t\t]
\t\t"visible" "bool" "1"
\t}
\t"visible" "bool" "1"
\t"children" "element_array"
\t[
\t]
}

"DmeVertexData"
{
\t"id" "elementid" "${I.bind}"
\t"name" "string" "bind"
\t"vertexFormat" "string_array"
\t[
\t\t"position$0",
\t\t"texcoord$0",
\t\t"normal$0",
\t\t"tangent$0"
\t]
\t"jointCount" "int" "0"
\t"flipVCoordinates" "bool" "0"
\t"position$0" "vector3_array"
\t[
${q(positions.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"position$0Indices" "int_array"
\t[
${idx}
\t]
\t"texcoord$0" "vector2_array"
\t[
${q(uvs.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"texcoord$0Indices" "int_array"
\t[
${idx}
\t]
\t"normal$0" "vector3_array"
\t[
${q(normals.map((p) => p.map(f4).join(" ")), "\t\t")}
\t]
\t"normal$0Indices" "int_array"
\t[
${idx}
\t]
\t"tangent$0" "vector4_array"
\t[
${q(positions.map(() => "1 0 0 1"), "\t\t")}
\t]
\t"tangent$0Indices" "int_array"
\t[
${idx}
\t]
}
`;
}

// ---- textures -----------------------------------------------------------------------
const S = 128;
const smooth = (t) => { t = Math.max(0, Math.min(1, t)); return t * t * (3 - 2 * t); };
// contact: dense core under the ball, long soft falloff (ambient occlusion look)
const contactTex = (level) => png(S, S, (x, y, b, o) => {
  const d = Math.hypot((x + 0.5) / S * 2 - 1, (y + 0.5) / S * 2 - 1);
  const a = CONTACT_ALPHA * level * Math.pow(1 - smooth(d), 1.6);
  const v = clamp(255 * a); b[o] = v; b[o + 1] = v; b[o + 2] = v;
});
// sun: a full-strength ellipse with a soft rim of SUN_SOFT units
const sunW = SUN_A + SUN_SOFT, sunH = SUN_B + SUN_SOFT;
const sunTex = png(S, S, (x, y, b, o) => {
  const px = ((x + 0.5) / S * 2 - 1) * sunW, py = ((y + 0.5) / S * 2 - 1) * sunH;
  const r = Math.hypot(px / SUN_A, py / SUN_B);          // 1 on the ellipse
  const edge = (r - 1) * SUN_B;                           // units outside (roughly)
  const a = SUN_ALPHA * (1 - smooth((edge + SUN_SOFT) / (2 * SUN_SOFT)));
  const v = clamp(255 * a); b[o] = v; b[o + 1] = v; b[o + 2] = v;
});
const black = png(8, 8, (x, y, b, o) => { b[o] = 0; b[o + 1] = 0; b[o + 2] = 0; });

// ---- models -------------------------------------------------------------------------
const quadMesh = (hw, hh) => ({
  positions: [[-hw, -hh, 0], [hw, -hh, 0], [hw, hh, 0], [-hw, hh, 0]],
  uvs: [[0, 1], [1, 1], [1, 0], [0, 0]],
  normals: [[0, 0, 1], [0, 0, 1], [0, 0, 1], [0, 0, 1]],
  faces: [[0, 1, 2, 3]],
});
const vmat = (trans) => `"Layer0"
{
	"shader"	"csgo_complex.vfx"
	"F_TRANSLUCENT"	"1"
	"F_DO_NOT_CAST_SHADOWS"	"1"
	"g_bFogEnabled"	"1"
	"g_flMetalness"	"0.000"
	"g_vColorTint"	"[1.000000 1.000000 1.000000 0.000000]"
	"TextureColor"	"${MAT_DIR}/shadow_black.png"
	"TextureRoughness"	"[1.000000 1.000000 1.000000 0.000000]"
	"TextureTranslucency"	"${trans}"
}
`;
const vmdl = (model, mat0, groups) => `<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc28:version{fb63b6ca-f435-4aa0-a2c7-c66ddc651dca} -->
{
\trootNode =
\t{
\t\t_class = "RootNode"
\t\tchildren =
\t\t[
\t\t\t{
\t\t\t\t_class = "MaterialGroupList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "DefaultMaterialGroup"
\t\t\t\t\t\tremaps = [  ]
\t\t\t\t\t\tuse_global_default = false
\t\t\t\t\t\tglobal_default_material = ""
\t\t\t\t\t},
${groups.map((m, i) => `\t\t\t\t\t{
\t\t\t\t\t\t_class = "MaterialGroup"
\t\t\t\t\t\tname = "level${i + 1}"
\t\t\t\t\t\tremaps =
\t\t\t\t\t\t[
\t\t\t\t\t\t\t{
\t\t\t\t\t\t\t\tfrom = "${mat0}.vmat"
\t\t\t\t\t\t\t\tto = "${m}.vmat"
\t\t\t\t\t\t\t},
\t\t\t\t\t\t]
\t\t\t\t\t},
`).join("")}\t\t\t\t]
\t\t\t},
\t\t\t{
\t\t\t\t_class = "RenderMeshList"
\t\t\t\tchildren =
\t\t\t\t[
\t\t\t\t\t{
\t\t\t\t\t\t_class = "RenderMeshFile"
\t\t\t\t\t\tname = "${path.basename(model)}"
\t\t\t\t\t\tfilename = "${model}.dmx"
\t\t\t\t\t},
\t\t\t\t]
\t\t\t},
\t\t]
\t}
}
`;
const write = (rel, data) => { const f = path.join(out, rel); fs.mkdirSync(path.dirname(f), { recursive: true }); fs.writeFileSync(f, data); };

write(`${MAT_DIR}/shadow_black.png`, black);
const contactMats = CONTACT_LEVELS.map((level, i) => {
  const mat = `${MAT_DIR}/contact_shadow_${i}`;
  write(`${mat}_trans.png`, contactTex(level));
  write(`${mat}.vmat`, vmat(`${mat}_trans.png`));
  return mat;
});
const c = quadMesh(CONTACT_R, CONTACT_R);
write(`${MODEL_C}.dmx`, dmxFor("contact_shadow", c.positions, c.uvs, c.normals, c.faces, contactMats[0]));
write(`${MODEL_C}.vmdl`, vmdl(MODEL_C, contactMats[0], contactMats.slice(1)));

const sunMat = `${MAT_DIR}/sun_shadow`;
write(`${sunMat}_trans.png`, sunTex);
write(`${sunMat}.vmat`, vmat(`${sunMat}_trans.png`));
const s = quadMesh(sunW, sunH);
write(`${MODEL_S}.dmx`, dmxFor("sun_shadow", s.positions, s.uvs, s.normals, s.faces, sunMat));
write(`${MODEL_S}.vmdl`, vmdl(MODEL_S, sunMat, []));
console.log(`ball shadows: contact r ${CONTACT_R} (${CONTACT_LEVELS.length} skins), sun ${(2 * sunW).toFixed(1)} x ${(2 * sunH).toFixed(1)} -> ${out}`);
