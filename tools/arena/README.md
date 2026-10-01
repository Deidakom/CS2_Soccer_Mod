# arena — the SoccerMod Arena map (`ka_soccermod_stadium`)

Our own stadium round the unchanged v8 pitch: a round two-tier bowl, wave roof with a glass
edge, lounges, video walls, dugouts, a tunnel head with the (shut) v8 door. Everything is
generated; nothing is drawn by hand and nothing comes from another map.

Addon: `csgo_addons/cs2sm_stadium_v1` (content + game), map `maps/ka_soccermod_stadium.vmap`.
Base: `maps/soccer_soccermod_stadium.vmap.bak-before-xsl` (our v8-based stadium source).

## What stays, what goes

Byte-identical to v8 inside the pitch walls: grass area ±1280 × ±1664 at z −32, lines, goals,
nets (func_brush), the low boards, the invisible walls, spawns, ball, the 34 pitch lights, sun 45/45.
Removed: old stands and roof, the underground, the sky path, wall score buttons, the marker gadget.
The main door stays as a solid `func_brush` in the west tunnel head.
The pitch floor gets `materials/soccermod_arena/pitch_grass` (same squares and surface, 2048 px per
166.4-unit tile = 12 texels per unit instead of 3).

## Files

| file | does |
| --- | --- |
| `layout.mjs` | all numbers: front curve (superellipse 1604 × 2070, exponent 4.5), cross-section, roof, seat rows |
| `geometry.mjs` | builds every face (stands, seats, roof, screens, tunnel heads, dugouts, ground) |
| `generate-arena.mjs` | writes the 8 sector models (+ collision), the preview scene, `arena-layout.json` |
| `generate-arena-textures.mjs` | every texture and material (`render-arena-graphics.ps1` first: boards, video wall, sign) |
| `generate-arena-radar.mjs` | radar image + overview (same frame as the v8 radar) |
| `apply-arena-vmap.mjs` | turns the base vmap into the arena vmap (removals, door, sky box, lights, props) |
| `generate-arena-crowd.mjs` | crowd models for the plugin (8 sections, own fan atlas `lib/fans.mjs`) |
| `generate-arena-plugin-assets.mjs` | LED ring segment and the four pitch designs as geometry |
| `generate-arena-plugin.mjs` | `SoccerModMvpPlugin.ArenaLayout.cs` (ring spots, banners, anchors, screens, rows) |
| `plugin/apply_arena_switches.py` | the small v8-or-arena switches, applied in place in the plugin tree |
| `package-arena.mjs` | the Workshop item as one VPK (follows the references from the map) |

## Build

```powershell
$cs = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
$a  = "$cs\content\csgo_addons\cs2sm_stadium_v1"; $rc = "$cs\game\bin\win64\resourcecompiler.exe"
powershell -ExecutionPolicy Bypass -File tools\arena\render-arena-graphics.ps1 <graphics dir>
node tools\arena\generate-arena-textures.mjs --graphics <graphics dir> --addon $a
node tools\arena\generate-arena.mjs --addon $a --layout <arena-layout.json>
node tools\arena\generate-arena-radar.mjs $a
node tools\arena\generate-arena-crowd.mjs $a
node tools\arena\generate-arena-plugin-assets.mjs $a
node --max-old-space-size=6144 tools\arena\apply-arena-vmap.mjs --in "$a\maps\soccer_soccermod_stadium.vmap.bak-before-xsl" --layout <arena-layout.json> --out "$a\maps\ka_soccermod_stadium.vmap"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\materials\soccermod_arena\*.vmat"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\models\soccermod_arena\*.vmdl"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\models\soccermod\atmo\crowd_arena\*.vmdl"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\panorama\images\overheadmaps\ka_soccermod_stadium_radar_psd.vtex"
& $rc -nop4 -game "$cs\game\csgo" -addon cs2sm_stadium_v1 -fshallow -i "$a\maps\ka_soccermod_stadium.vmap"   # about 4 minutes
node tools\arena\package-arena.mjs --addon "$cs\game\csgo_addons\cs2sm_stadium_v1" --out <item.vpk>
```

## Looking at it without a server

`cs2.exe -tools -addon cs2sm_stadium_v1`, then in the console `map_workshop cs2sm_stadium_v1 ka_soccermod_stadium`
(`map <name>` answers "invalid map name"; the console command `maps` crashes the tools build).
`tools/cs2-vconsole.mjs` sends console commands (port 29000). What the plugin would spawn can be
placed by hand: `ent_create prop_dynamic {"model" "models/soccermod/atmo/crowd_arena/end_red_lower.vmdl" "origin" "0 0 0"}`.

## Things learnt

- Static models are lightmapped like world geometry; a full map build takes about 4 minutes.
- `F_DO_NOT_CAST_SHADOWS` on a material does not keep it out of the dynamic (cascade) shadows:
  thin roof steel drew lines over the plugin's design floor. The arena props have `disableshadows 1`.
- The plugin's bake grass carries v8's roof shadow; on the arena the lit "fine" tiles are used.
- Dynamic props (design floors) are lit with one sample each; they match the map floor because the
  pitch is evenly lit.
