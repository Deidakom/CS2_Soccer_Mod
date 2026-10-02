# street — the SoccerMod street arena (`soccer_street_arena`)

A street court for three against three, generated from scratch like the other maps (`tools/arena`,
`tools/hall`, `tools/gym`). Owner 2026-10-02: street soccer in the city, "a bit of the flair of
Brazil, a bit of New York", graffiti instead of LED banners, FIFA Street style, onlookers "really
from the streets", no fireworks. Addon: `csgo_addons/cs2sm_stadium_v1`, map
`maps/soccer_street_arena.vmap`.

After his first look (the same day): "keep the scenario to Brazil, remove the skyscrapers, add a Brazil
flair behind the other goal"; "for the ceiling I think we can remove it, just make sure the ball can't
get out"; an eye-catcher of its own - his pick: a painted court and a canopy of flags, then: "more
subtle, more abstract and graffiti-like" for the painting.

## What is in it

- An asphalt court of 1300 × 1800 units at golden hour. Walls and fences are in play; above them
  invisible walls go on up to an invisible lid (`COURT.ceil`): the ball never leaves the court.
- Over the court: twelve strings of festival flags and bulbs that swing in the wind, six kites over
  the roofs (one moving model, `generate-street-canopy.mjs`, clip `sway`, placed by the map).
- The ground: worn asphalt with one faded sprayed painting over the whole court (`g_court`: loose
  rays, rings, dots - thin, the asphalt shows through) and a few abstract pieces (`a_*`), two
  numbers, a chalk hopscotch, the white lines.
- South: houses in bright colours on three terraces up a hill (their colours follow the rays of the
  court's painting), the tiled stairway, a kiosk-bar; behind them the painted hill with more houses
  and a statue on its peak. The metro line disappears into a tunnel in the hill.
- North: a cobbled square, a row of colonial houses with balconies and a church with two towers,
  palms; far behind them the bay's round rock with its cable car.
- East: the court lies under a **metro viaduct** on riveted steel columns; every 45 seconds a
  **train** runs over it (an animated model, clip `run`). Apartment blocks with balconies and
  washing behind it, shops with Portuguese signs, a cab.
- West: the street of shops (mercado, oficina, barbearia, padaria), a wave-mosaic sidewalk, an old
  two-tone van, palms, lamp posts; a dark ridge with palms against the sunset.
- **Goals**: v8's mouth (248 × 97) inside two cut-open shipping containers.
- **Graffiti**: burners along the retaining wall under the viaduct, murals on the end walls, tags on
  the low wall, pillars, shutters, the train.
- 135 **onlookers** in street clothes (no team colours) on the sidewalks, the containers, the
  balconies, the church steps, the stairway and the roofs. They cheer at goals (plugin).

## Files

| file | does |
| --- | --- |
| `layout.mjs` | all numbers; the painted lines |
| `geometry.mjs` | every face in eight parts (court, cage, west, east, north, south, props, far); collision as plain boxes; onlooker rows, lights; `palm()` |
| `generate-street.mjs` | the part models (`street_court` and `street_cage` carry the collision), preview scene, layout file |
| `render-street-art.ps1` | everything painted or written: graffiti pieces, characters, tags, ground paintings, shop fronts, signs (PNG, the sprayed ones with alpha) |
| `generate-street-textures.mjs` | all materials; the walls with graffiti are composed here (wall + pieces), the train's sheet, the sunset sky |
| `street-fans.mjs` | the onlookers' figure sheet (same layout as the stadium's fan atlas) |
| `generate-street-train.mjs` | the train (one bone, clip `run`) |
| `generate-street-canopy.mjs` | the flags and kites (one bone per string and per kite, clip `sway`) |
| `generate-street-radar.mjs` | radar picture and overview |
| `plugin/apply_street.py` | map profile `Street`, no pitch designs, own sky, default map list |

From the indoor hall's tools: `generate-hall-crowd.mjs --atlas street --height 70` (onlookers) and
`apply-hall-vmap.mjs` (the vmap: `layout.sun`, `layout.sky`, `layout.props`, probe volume).

## Build

```powershell
$cs = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
$a  = "$cs\content\csgo_addons\cs2sm_stadium_v1"; $rc = "$cs\game\bin\win64\resourcecompiler.exe"
powershell -ExecutionPolicy Bypass -File tools\street\render-street-art.ps1 <art dir>
node tools\street\generate-street-textures.mjs --art <art dir> --addon $a
node tools\street\generate-street.mjs --addon $a --layout <street-layout.json>
node tools\hall\generate-hall-crowd.mjs $a <street-layout.json> --dir models/soccermod_street --mat materials/soccermod_street/crowd.vmat --sections east,west,end_red,end_blue --atlas street --height 70
node tools\street\generate-street-train.mjs $a
node tools\street\generate-street-radar.mjs $a
node --max-old-space-size=6144 tools\hall\apply-hall-vmap.mjs --in "$a\maps\soccer_soccermod_arena.vmap" --layout <street-layout.json> --out "$a\maps\soccer_street_arena.vmap"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\materials\soccermod_street\*.vmat"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\models\soccermod_street\*.vmdl"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\panorama\images\overheadmaps\soccer_street_arena_radar_psd.vtex"
& $rc -nop4 -game "$cs\game\csgo" -addon cs2sm_stadium_v1 -fshallow -i "$a\maps\soccer_street_arena.vmap"
node tools\arena\package-arena.mjs --addon "$cs\game\csgo_addons\cs2sm_stadium_v1" --map soccer_street_arena --models models/soccermod_street --out <street.vpk>
```

Copy `resource/overviews/soccer_street_arena.txt` into the game addon folder before packaging.
The map compile takes about 13 minutes, nearly all of it visibility for the open space (about
50,000 clusters); the log is written in blocks, so it looks stuck at "Preprocessing Lights".

## Looking at it

Without the game: `generate-street.mjs --preview <dir>`, `generate-street-textures.mjs --preview
<dir>`, the crowd and train generators with the preview dir, then the three.js viewer with a low
sun and the sky picture (session scratch `view/street.html`).
