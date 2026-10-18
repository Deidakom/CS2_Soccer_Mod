// The .vmat text for a generated material (pictures named <dir>/<name>_color/_normal/_trans/_illum.png).
// m: { rough, metal?, alphaTest?, translucent?, illum?, twoSided?, noShadow?, pitch? }; has: which pictures exist.
const f3 = (v) => v.toFixed(6);
export function vmat(DIR, name, m, has) {
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
