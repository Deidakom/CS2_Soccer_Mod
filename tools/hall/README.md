# hall — the SoccerMod indoor hall (`ka_soccermod_indoor`)

A boarded indoor court for 3 to 4 players per team, generated from scratch like the stadium
(`tools/arena`): nothing is drawn by hand and nothing comes from another map. Addon:
`csgo_addons/cs2sm_stadium_v1` (same as the stadium), map `maps/ka_soccermod_indoor.vmap`.

## What is in it

- Court 1680 × 2300 with rounded corners, boards (44 high) with LED adverts, acrylic panels and
  nets up to a ceiling net (492): the ball never leaves the court.
- v8's goal mouth (248 × 97) set into the end boards, with a housing deep enough for the plugin's
  moving net and net pocket. Goal line = the boards' inner face (y ±1150).
- Turf drawn blade by blade (2048 px per 170.67 units = 12 texels per unit), two tones in bands, white lines.
- Main stand (8 rows) with a glass-fronted lounge above, east stand (5 rows) with the hall's mural and a
  window band, standing terraces behind both goals, team shelters with bench and coach, 1,676 fans
  (cut-out cards from the stadium's fan atlas, animated models).
- Barrel roof with trusses, a skylight along the ridge, 44 LED light bars, a video cube over the centre
  spot, screens and banners on the end walls, light columns in the corners.

## Files

| file | does |
| --- | --- |
| `layout.mjs` | all numbers; the board line (rounded rectangle) |
| `geometry.mjs` | every face, grouped into parts; collision as plain boxes (`physOnly`); crowd rows, lights |
| `generate-hall.mjs` | the part models (`hall_pitch` and `hall_court` carry the collision), preview scene, layout file |
| `generate-hall-textures.mjs` | materials (`render-hall-graphics.ps1` first; needs the stadium's `ads_a.png` / `ads_b.png`) |
| `generate-hall-crowd.mjs` | four crowd models (clips idle / cheer / wave / goal_red / goal_blue) |
| `generate-hall-radar.mjs` | radar picture and overview |
| `apply-hall-vmap.mjs` | the vmap, from element templates of the stadium's vmap |
| `plugin/apply_hall_profile.py` | the map profile for the plugin (MapProfiles.cs), applied in place |

Shared with the stadium: `tools/arena/lib/*` (mesh, dmx, img, grass, fans, recipes), `tools/arena/package-arena.mjs`.

## Build

```powershell
$cs = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
$a  = "$cs\content\csgo_addons\cs2sm_stadium_v1"; $rc = "$cs\game\bin\win64\resourcecompiler.exe"
powershell -ExecutionPolicy Bypass -File tools\hall\render-hall-graphics.ps1 <graphics dir>   # + copy ads_a.png, ads_b.png there
node tools\hall\generate-hall-textures.mjs --graphics <graphics dir> --addon $a
node tools\hall\generate-hall.mjs --addon $a --layout <hall-layout.json>
node tools\hall\generate-hall-crowd.mjs $a <hall-layout.json>
node tools\hall\generate-hall-radar.mjs $a
node --max-old-space-size=6144 tools\hall\apply-hall-vmap.mjs --in "$a\maps\ka_soccermod_stadium.vmap" --layout <hall-layout.json> --out "$a\maps\ka_soccermod_indoor.vmap"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\materials\soccermod_hall\*.vmat"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\models\soccermod_hall\*.vmdl"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\panorama\images\overheadmaps\ka_soccermod_indoor_radar_psd.vtex"
& $rc -nop4 -game "$cs\game\csgo" -addon cs2sm_stadium_v1 -fshallow -i "$a\maps\ka_soccermod_indoor.vmap"   # about 6 minutes
node tools\arena\package-arena.mjs --addon "$cs\game\csgo_addons\cs2sm_stadium_v1" --map ka_soccermod_indoor --models models/soccermod_hall --out <hall.vpk>
```

Copy `resource/overviews/ka_soccermod_indoor.txt` into the game addon folder before packaging.

## Looking at it

Without the game: `generate-hall.mjs --preview <dir>`, `generate-hall-textures.mjs --preview <dir>`,
`generate-hall-crowd.mjs <addon> <layout> <dir>` write a scene for a three.js viewer (the stadium's
previewer with indoor lighting). In the game: `cs2.exe -tools -addon cs2sm_stadium_v1`, then
`map_workshop cs2sm_stadium_v1 ka_soccermod_indoor`.

## Not checked in the game yet (2026-10-01)

Lighting levels (no sun: 44 baked lights over the court plus stand lights and lit materials), the
fans' `prop_dynamic` entities (made from the prop_static template), ball behaviour on the model
collision. First thing to look at once CS2 is free on this PC.
