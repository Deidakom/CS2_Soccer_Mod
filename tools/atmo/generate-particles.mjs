#!/usr/bin/env node
// "Arena Vision" (2026-09-29, v8 stadium only): our own particle systems for
// the goal choreography. Only CS2's own textures are used, so the Feature
// Package gets tiny text files. Writes KV3 .vpcf sources into
// src/workshop-addon/soccermod_atmo/particles/soccermod/atmo/.
//
// Engine-lab finding: CS2's firework_crate particles are 2-6 units big and
// invisible in a stadium; these are sized for 1000-3000 unit views.
//
// usage: node tools/atmo/generate-particles.mjs
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const outDir = path.join(root, "src/workshop-addon/soccermod_atmo/particles/soccermod/atmo");
fs.mkdirSync(outDir, { recursive: true });

const HEADER = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:vpcf54:version{326b1595-45e8-4004-aa5a-3e08655ff51f} -->";
const TEX = {
  flare: "materials/effects/yellowflare.vtex",
  glow: "materials/particle/particle_glow_05.vtex",
  smoke: "materials/particle/particle_smokegrenade.vtex",
  // CS2 confetti.vtex is a 2048x64 strip without sheet data: RandomSequence on it crashed the client (2026-09-29).
  // paper.vtex has a real 10-sequence sheet (crumpled paper), tinted by team colour.
  confetti: "materials/particle/paper/paper.vtex",
};
const TEAM = {
  red: { main: [255, 50, 30], light: [255, 150, 110], hot: [255, 120, 60] },
  blue: { main: [40, 110, 255], light: [150, 200, 255], hot: [120, 180, 255] },
  gold: { main: [255, 190, 60], light: [255, 235, 150], hot: [255, 220, 120] },
};

const v3 = (a) => `[ ${a.map((n) => Number(n).toFixed(1)).join(", ")} ]`;
const c4 = (a, alpha = 255) => `[ ${a[0]}, ${a[1]}, ${a[2]}, ${alpha} ]`;
const rnd = (min, max, field, extra = "") => `
		{
			_class = "C_INIT_InitFloat"
			m_InputValue =
			{
				m_nType = "PF_TYPE_RANDOM_UNIFORM"
				m_flRandomMin = ${min.toFixed(3)}
				m_flRandomMax = ${max.toFixed(3)}
				m_nRandomMode = "PF_RANDOM_MODE_CONSTANT"${extra}
			}
			m_nOutputField = ${field}
		},`;
const LIFETIME = 1, RADIUS = 3, ROLL = 4;

function renderer(tex, blend) {
  return `
		{
			_class = "C_OP_RenderSprites"
			m_flDiffuseAmount = 0.0
			m_flSelfIllumAmount = 1.0
			m_vecTexturesInput =
			[
				{
					m_hTexture = resource:"${tex}"
				},
			]
			m_nOutputBlendMode = "${blend}"
		},`;
}

function system({ max, renderers, operators, initializers, emitters, children = [], forces = [] }) {
  return `${HEADER}
{
	_class = "CParticleSystemDefinition"
	m_bShouldHitboxesFallbackToRenderBounds = false
	m_nMaxParticles = ${max}
	m_flConstantRadius = 8.0
	m_flMaxDrawDistance = 12000.0
	m_Renderers =
	[${renderers.join("")}
	]
	m_Operators =
	[${operators.join("")}
	]
	m_Initializers =
	[${initializers.join("")}
	]
	m_ForceGenerators =
	[${forces.join("")}
	]
	m_Emitters =
	[${emitters.join("")}
	]
	m_Children =
	[${children.map((c) => `
		{
			m_ChildRef = resource:"particles/soccermod/atmo/${c}.vpcf"
		},`).join("")}
	]
	m_nBehaviorVersion = 5
	m_nFirstMultipleOverride_BackwardCompat = 4
}
`;
}

const instant = (n) => `
		{
			_class = "C_OP_InstantaneousEmitter"
			m_nParticlesToEmit =
			{
				m_nType = "PF_TYPE_LITERAL"
				m_flLiteralValue = ${n.toFixed(1)}
			}
		},`;
const continuous = (rate, seconds) => `
		{
			_class = "C_OP_ContinuousEmitter"
			m_flEmissionDuration =
			{
				m_nType = "PF_TYPE_LITERAL"
				m_flLiteralValue = ${seconds.toFixed(2)}
			}
			m_flEmitRate =
			{
				m_nType = "PF_TYPE_LITERAL"
				m_flLiteralValue = ${rate.toFixed(1)}
			}
		},`;
const movement = (gravity, drag) => `
		{
			_class = "C_OP_BasicMovement"
			m_fDrag = ${drag.toFixed(3)}
			m_Gravity = ${v3([0, 0, gravity])}
		},`;
const decay = `
		{
			_class = "C_OP_Decay"
		},`;
const fadeOut = (frac) => `
		{
			_class = "C_OP_FadeOutSimple"
			m_flFadeOutTime = ${frac.toFixed(2)}
		},`;
const fadeIn = (frac) => `
		{
			_class = "C_OP_FadeInSimple"
			m_flFadeInTime = ${frac.toFixed(2)}
		},`;
const radiusScale = (start, end, bias = 0.5) => `
		{
			_class = "C_OP_InterpolateRadius"
			m_flStartScale = ${start.toFixed(2)}
			m_flEndScale = ${end.toFixed(2)}
			m_flBias = ${bias.toFixed(2)}
		},`;
const colorFade = (rgb) => `
		{
			_class = "C_OP_ColorInterpolate"
			m_ColorFade = ${c4(rgb)}
		},`;
const spin = (rate) => `
		{
			_class = "C_OP_OscillateScalar"
			m_nField = ${ROLL}
			m_RateMin = ${(-rate).toFixed(1)}
			m_RateMax = ${rate.toFixed(1)}
			m_FrequencyMin = 0.5
			m_FrequencyMax = 3.0
		},`;
const flutter = (f) => `
		{
			_class = "C_OP_RandomForce"
			m_MinForce = ${v3([-f, -f, -f * 0.5])}
			m_MaxForce = ${v3([f, f, f * 0.5])}
		},`;
const color = (a, b) => `
		{
			_class = "C_INIT_RandomColor"
			m_ColorMin = ${c4(a)}
			m_ColorMax = ${c4(b)}
		},`;
const alpha = (a, b) => `
		{
			_class = "C_INIT_RandomAlpha"
			m_nAlphaMin = ${a}
			m_nAlphaMax = ${b}
		},`;
const sphere = (radius, speedMin, speedMax, bias = [1, 1, 1], localMin = null, localMax = null) => `
		{
			_class = "C_INIT_CreateWithinSphereTransform"
			m_fRadiusMax = ${radius.toFixed(1)}
			m_vecDistanceBias = ${v3(bias)}
			m_fSpeedMin = ${speedMin.toFixed(1)}
			m_fSpeedMax = ${speedMax.toFixed(1)}${localMin ? `
			m_LocalCoordinateSystemSpeedMin = ${v3(localMin)}
			m_LocalCoordinateSystemSpeedMax = ${v3(localMax)}` : ""}
		},`;
// Directed start velocity as in CS2 own particles (the Local speed fields of
// CreateWithinSphereTransform left confetti hanging at the origin, 2026-09-29).
const velocity = (min, max) => `
		{
			_class = "C_INIT_InitialVelocityNoise"
			m_flNoiseScale = 23.0
			m_flNoiseScaleLoc = 23.0
			m_vecOutputMin = ${v3(min)}
			m_vecOutputMax = ${v3(max)}
			m_TransformInput = 
			{
				m_nType = "PT_TYPE_INVALID"
			}
		},`;
const sequence = (max) => `
		{
			_class = "C_INIT_RandomSequence"
			m_nSequenceMin = 0
			m_nSequenceMax = ${max}
			m_bShuffle = true
		},`;
const signFlip = `
				m_bHasRandomSignFlip = true`;

const files = {};

for (const [team, col] of Object.entries(TEAM)) {
  // Firework burst: a sphere of glowing sparks that falls and fades (spawn it high in the sky).
  files[`firework_${team}`] = system({
    max: 700,
    renderers: [renderer(TEX.flare, "PARTICLE_OUTPUT_BLEND_MODE_ADD")],
    operators: [movement(-160, 0.035), decay, fadeOut(0.45), radiusScale(1.0, 0.25, 0.6), colorFade(col.main)],
    initializers: [sphere(12, 420, 620), rnd(1.6, 2.6, LIFETIME), rnd(14, 24, RADIUS), color(col.light, [255, 255, 255]), rnd(0, 360, ROLL, signFlip)],
    emitters: [instant(600)],
    children: [`firework_flash_${team}`],
  });
  files[`firework_flash_${team}`] = system({
    max: 4,
    renderers: [renderer(TEX.glow, "PARTICLE_OUTPUT_BLEND_MODE_ADD")],
    operators: [decay, fadeOut(0.8), radiusScale(0.4, 1.6, 0.3)],
    initializers: [sphere(0, 0, 0), rnd(0.35, 0.45, LIFETIME), rnd(500, 600, RADIUS), color(col.hot, col.light)],
    emitters: [instant(2)],
  });
  // gold: the unlockable "Gold rain" / "Legend" goal celebrations (AtmoGoalFx.cs)
  // Confetti cannon: a column shooting up that floats down slowly, team colour mixed with white.
  files[`confetti_cannon_${team}`] = system({
    max: 900,
    renderers: [renderer(TEX.confetti, "PARTICLE_OUTPUT_BLEND_MODE_ALPHA")],
    // m_fDrag is a strong per-step damping: 0.9 froze the confetti at the cannon (2026-09-29).
    operators: [movement(-140, 0.045), spin(10), decay, fadeOut(0.15)],
    // RandomForce belongs in m_ForceGenerators; as an operator it crashed the client (2026-09-29).
    forces: [flutter(260)],
    initializers: [sphere(20, 0, 0), velocity([-260, -260, 900], [260, 260, 1400]), rnd(5.5, 8.0, LIFETIME), rnd(7, 11, RADIUS), sequence(9), color(col.main, [255, 255, 255]), rnd(0, 360, ROLL, signFlip)],
    emitters: [instant(800)],
  });
  // Confetti rain: a flat cloud high over a goal that falls for a few seconds.
  files[`confetti_rain_${team}`] = system({
    max: 1600,
    renderers: [renderer(TEX.confetti, "PARTICLE_OUTPUT_BLEND_MODE_ALPHA")],
    operators: [movement(-70, 0.06), spin(9), decay, fadeOut(0.12), fadeIn(0.05)],
    forces: [flutter(180)],
    initializers: [sphere(700, 0, 0, [1, 1, 0.08]), rnd(6.0, 9.0, LIFETIME), rnd(7, 11, RADIUS), sequence(9), color(col.main, [255, 255, 255]), rnd(0, 360, ROLL, signFlip)],
    emitters: [continuous(260, 4.0)],
  });
  // Flare (Bengalo): glowing core, sparks and team-coloured smoke, burns ~8 s.
  files[`flare_${team}`] = system({
    max: 40,
    renderers: [renderer(TEX.glow, "PARTICLE_OUTPUT_BLEND_MODE_ADD")],
    operators: [decay, fadeOut(0.5)],
    initializers: [sphere(3, 0, 0), rnd(0.08, 0.14, LIFETIME), rnd(34, 52, RADIUS), color(col.hot, [255, 255, 230])],
    emitters: [continuous(40, 8.0)],
    children: [`flare_sparks_${team}`, `flare_smoke_${team}`],
  });
  files[`flare_sparks_${team}`] = system({
    max: 160,
    renderers: [renderer(TEX.flare, "PARTICLE_OUTPUT_BLEND_MODE_ADD")],
    operators: [movement(-420, 0.02), decay, fadeOut(0.5), radiusScale(1.0, 0.3)],
    initializers: [sphere(4, 0, 0), velocity([-90, -90, 140], [90, 90, 320]), rnd(0.4, 0.9, LIFETIME), rnd(2.5, 4.5, RADIUS), color(col.hot, [255, 240, 200])],
    emitters: [continuous(120, 8.0)],
  });
  files[`flare_smoke_${team}`] = system({
    max: 70,
    renderers: [renderer(TEX.smoke, "PARTICLE_OUTPUT_BLEND_MODE_ALPHA")],
    operators: [movement(60, 0.02), decay, fadeIn(0.12), fadeOut(0.55), radiusScale(0.3, 2.4, 0.4), spin(0.6)],
    initializers: [sphere(6, 0, 0), velocity([-25, -25, 55], [25, 25, 95]), rnd(5.0, 7.5, LIFETIME), rnd(70, 100, RADIUS), color(col.main, col.light), alpha(90, 150), rnd(0, 360, ROLL, signFlip)],
    emitters: [continuous(7, 8.0)],
  });
}

// Ball power trail: the plugin parents it to the ball for a hard shot, so the glow
// particles are left behind along the flight path and fade within ~0.4 s.
for (const team of ["red", "blue"]) {
  const col = TEAM[team];
  files[`ball_trail_${team}`] = system({
    max: 220,
    renderers: [renderer(TEX.glow, "PARTICLE_OUTPUT_BLEND_MODE_ADD")],
    operators: [decay, fadeOut(0.7), radiusScale(1.0, 0.2, 0.5), colorFade(col.main)],
    initializers: [sphere(2, 0, 0), rnd(0.3, 0.45, LIFETIME), rnd(13, 18, RADIUS), color(col.light, [255, 255, 255])],
    emitters: [continuous(300, 2.0)],
  });
}

// Post / crossbar hit: a short burst of hot sparks at the contact point.
files.post_sparks = system({
  max: 90,
  renderers: [renderer(TEX.flare, "PARTICLE_OUTPUT_BLEND_MODE_ADD")],
  operators: [movement(-650, 0.02), decay, fadeOut(0.5), radiusScale(1.0, 0.3)],
  initializers: [sphere(3, 220, 520), rnd(0.25, 0.7, LIFETIME), rnd(2.5, 4.5, RADIUS), color([255, 230, 170], [255, 255, 255])],
  emitters: [instant(80)],
});

// Camera flashes across a stand: short white pops in a wide flat area.
files.camera_flashes = system({
  max: 120,
  renderers: [renderer(TEX.glow, "PARTICLE_OUTPUT_BLEND_MODE_ADD")],
  operators: [decay, fadeOut(0.6)],
  initializers: [sphere(1100, 0, 0, [1, 0.25, 0.25]), rnd(0.05, 0.11, LIFETIME), rnd(9, 16, RADIUS), color([255, 250, 235], [255, 255, 255])],
  emitters: [continuous(70, 3.5)],
});

for (const [name, text] of Object.entries(files)) fs.writeFileSync(path.join(outDir, `${name}.vpcf`), text);
console.log(`${Object.keys(files).length} particle systems -> ${outDir}`);
console.log(Object.keys(files).join(" "));
