# Arena Vision asset tools (v8 stadium atmosphere)

Generators for the content addon `soccermod_atmo` (compiled with `resourcecompiler.exe -nop4 -f -game <CS2>\game\csgo -i <files>`, shipped in the Feature Package 3797479770). All write into `<CS2>/content/csgo_addons/soccermod_atmo`:

- `generate-crowd-stadium.mjs` - the full crowd (8 section models, clips idle/cheer/wave/goal_red/goal_blue). Needs `.local/crowd/v8_treads.json` (tread triangles per row level) and `.local/crowd/v8_midsteps.json` (horizontal half steps between levels = stairs), both exported from the v8 world meshes (`vrf-dump3` triangle dump on the VPS; not in git).
- `render-board-pages.ps1` + `generate-led-boards.mjs` - LED board pages (1280 x 200) and the LED model with pixel-dissolve skins.
- `generate-ring.mjs`, `generate-banners.mjs` + `generate-banner-fabric.mjs`, `generate-dugouts.mjs`, `generate-spidercam.mjs`, `generate-particles.mjs` - light ring, waving roof banners, dugout subs and coaches, spidercam, goal pyro particles.
- `synth-crowd.mjs` - synthesised crowd sounds (applause, roar, ooh, chant); murmur and fans' whistle are owner-supplied recordings.
- `dmx-lib.mjs` - shared DMX mesh/animation/vmdl writers.

`generate-boards.mjs` and `generate-crowd.mjs` are the first versions (dot-font boards, one test section), kept for reference.
