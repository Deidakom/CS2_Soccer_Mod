# pool — the SoccerMod 1v1 cage (`soccer_1v1_cage`)

One against one, generated from scratch like the other maps (`tools/arena`, `tools/hall`,
`tools/gym`, `tools/street`). Owner 2026-10-02: "a 1v1 map that is built in a cage, it can also be
a mix of underground and indoor scenario ... well designed outstanding idea, jaw-dropping details
and just fun to play". His pick: a drained swimming pool in an old bathhouse. Addon:
`csgo_addons/cs2sm_stadium_v1`, map `maps/soccer_1v1_cage.vmap`, Workshop item 3811824498.

## What is in it

- The pitch is the pool's bottom, 600 × 880 units. The tiled walls (120 high) are in play; a steel
  cage stands on the pool's edge (mesh up to 300, a mesh lid = the ball's ceiling).
- The 2v2 goal of soccer_multi_indoor (156 × 62) set into the pool's end walls as a niche of dark
  tiles with a lamp in its roof, a white steel frame and a net - the plugin adds no goal extras.
- Round lamps glow in the pool's walls (16, each with a baked light); four warm work lights on
  tripods stand on the deck's corners, fed by a generator; bulbs along the gallery rails.
- Lane lines of black tiles on the floor with the painted pitch lines over them, the main drain on
  the centre spot, wet patches, a dark band of tiles under the edge with depth markings and signs,
  ladders, rust runs under the lamps, graffiti on the tiles (pieces of the street arena's art).
- Round the pool: the deck, an arcade of columns with changing cabins behind it, a gallery above,
  the diving tower with two platforms and a springboard at the home end, five starting blocks at
  the away end, a mosaic, a clock, the players' chalk scoreboard (HOME / AWAY), a vault with a
  glass middle and the night sky behind it.
- 139 onlookers in street clothes at the cage, along the gallery and on the tower.

## Files

| file | does |
| --- | --- |
| `layout.mjs` | all numbers; the lane lines and the pitch lines |
| `geometry.mjs` | every face in five parts (pool, cage, deck, hall, props); the pool part carries all collision; onlooker rows, lights |
| `generate-pool.mjs` | the part models, preview scene, layout file |
| `render-pool-graphics.ps1` | the pictures with lettering: signs, cabins, clock, scoreboard, block numbers |
| `generate-pool-textures.mjs` | all materials (tiles, mosaic, cut-outs, the night sky); `--street-art <dir>` for the graffiti |
| `generate-pool-radar.mjs` | radar picture and overview |
| `plugin/apply_pool.py` | map profile `Pool`, no pitch designs, default map list (`-` as the id: no list entry) |

From the other maps' tools: `tools/hall/generate-hall-crowd.mjs --atlas street --height 70`
(onlookers), `tools/hall/apply-hall-vmap.mjs` (the vmap), `tools/arena/package-arena.mjs`,
`tools/arena/build-map-item.sh` (the Workshop item = feature base + this map).

## Build

```powershell
$cs = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
$a  = "$cs\content\csgo_addons\cs2sm_stadium_v1"; $rc = "$cs\game\bin\win64\resourcecompiler.exe"
powershell -ExecutionPolicy Bypass -File tools\pool\render-pool-graphics.ps1 <graphics dir>
node tools\pool\generate-pool-textures.mjs --graphics <graphics dir> --street-art <street art dir> --addon $a
node tools\pool\generate-pool.mjs --addon $a --layout <pool-layout.json>
node tools\hall\generate-hall-crowd.mjs $a <pool-layout.json> --dir models/soccermod_pool --mat materials/soccermod_pool/crowd.vmat --sections east,west,end_red,end_blue --atlas street --height 70
node tools\pool\generate-pool-radar.mjs $a
node --max-old-space-size=6144 tools\hall\apply-hall-vmap.mjs --in "$a\maps\soccer_soccermod_arena.vmap" --layout <pool-layout.json> --out "$a\maps\soccer_1v1_cage.vmap"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\materials\soccermod_pool\*.vmat"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\models\soccermod_pool\*.vmdl"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\panorama\images\overheadmaps\soccer_1v1_cage_radar_psd.vtex"
& $rc -nop4 -game "$cs\game\csgo" -addon cs2sm_stadium_v1 -fshallow -i "$a\maps\soccer_1v1_cage.vmap"
node tools\arena\package-arena.mjs --addon "$cs\game\csgo_addons\cs2sm_stadium_v1" --map soccer_1v1_cage --models models/soccermod_pool --out <map-only.vpk>
```

Copy `resource/overviews/soccer_1v1_cage.txt` into the game addon folder before packaging.
