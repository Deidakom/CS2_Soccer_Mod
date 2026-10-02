# gym — the SoccerMod 2v2 hall (`soccer_2v2_arena`)

A small sports hall for two against two, generated from scratch like the stadium (`tools/arena`)
and the indoor hall (`tools/hall`). Addon: `csgo_addons/cs2sm_stadium_v1`, map
`maps/soccer_2v2_arena.vmap`, Workshop item 3811585232. (The folder and the material / model
directories are called "gym": that was the working name.)

Since update 3 (owner 2026-10-02: "give it a catch that stands out" - his pick: a rooftop hall over the
city and a painted floor; "it's not a cage, rename the banners"; HOME / AWAY for RED / BLUE) the hall
stands on the roof of a high-rise.

## What is in it

- Parquet court, inner faces at x ±520 and y ±720. The end walls are in play; the long sides
  are a cage (kick boards 40 high, steel mesh to 210, a net to 284) with a net under the beams:
  the ball never leaves the court.
- The 2v2 goal of soccer_multi_indoor (mouth 156 × 62) set into the end walls, 72 deep, with its
  own striped frame and net - the plugin adds no goal extras here.
- The parquet's painting (`ART` in the layout; stained wood, the boards and their grain run on
  through it): a broad ring of darker wood round each goal area and one round the centre circle,
  each with a thin accent ring. The two goal areas in white stain, an emblem in the centre circle,
  and only the game's white lines. (Update 3 had big free shapes in four strong colours - owner:
  "too abstract, tone it down, more subtle".)
- East: a glass front from the floor to the roof and a glass strip in the roof. Outside a roof terrace
  (gravel, parapet, cooling units, an aerial, a plant room at each end) and the city in the evening as
  two painted layers with lit windows (`city_near`, `city_far`); the map's own dusk sky (`sky_dusk`).
  The sky box takes the terrace and the city in, so the map compile takes about six minutes.
- 354 fans on three steps behind the cage on both sides (animated cut-out models, as in the hall).
- Glulam beams, LED panels, a window band in the west, wall bars, basketball boards (solid), a
  scoreboard and banners for HOME and AWAY, signs reading ROOFTOP ARENA.
- A light probe volume, so the ball and the players are lit by the hall's lamps.

## Files

| file | does |
| --- | --- |
| `layout.mjs` | all numbers; the painted lines |
| `geometry.mjs` | every face in four parts (floor, cage, hall, outside); collision as plain boxes (`physOnly`); crowd rows, lights |
| `generate-gym.mjs` | the part models (`gym_floor` and `gym_cage` carry the collision), preview scene, layout file |
| `generate-gym-textures.mjs` | materials (`render-gym-graphics.ps1` first) |
| `generate-gym-radar.mjs` | radar picture and overview |
| `plugin/apply_gym_profile.py` | map profile `Gym` for the plugin |

From the indoor hall's tools: `tools/hall/generate-hall-crowd.mjs` (fans) and
`tools/hall/apply-hall-vmap.mjs` (the vmap; spawns, team select, fans and the probe volume come
from the layout file). Shared: `tools/arena/lib/*`, `tools/arena/package-arena.mjs`.

## Build

```powershell
$cs = "E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive"
$a  = "$cs\content\csgo_addons\cs2sm_stadium_v1"; $rc = "$cs\game\bin\win64\resourcecompiler.exe"
powershell -ExecutionPolicy Bypass -File tools\gym\render-gym-graphics.ps1 <graphics dir>
node tools\gym\generate-gym-textures.mjs --graphics <graphics dir> --addon $a
node tools\gym\generate-gym.mjs --addon $a --layout <gym-layout.json>
node tools\hall\generate-hall-crowd.mjs $a <gym-layout.json> --dir models/soccermod_gym --mat materials/soccermod_gym/crowd.vmat --sections east,west
node tools\gym\generate-gym-radar.mjs $a
node --max-old-space-size=6144 tools\hall\apply-hall-vmap.mjs --in "$a\maps\soccer_soccermod_arena.vmap" --layout <gym-layout.json> --out "$a\maps\soccer_2v2_arena.vmap"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\materials\soccermod_gym\*.vmat"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\models\soccermod_gym\*.vmdl"
& $rc -nop4 -f -game "$cs\game\csgo" -i "$a\panorama\images\overheadmaps\soccer_2v2_arena_radar_psd.vtex"
& $rc -nop4 -game "$cs\game\csgo" -addon cs2sm_stadium_v1 -fshallow -i "$a\maps\soccer_2v2_arena.vmap"   # under a minute
node tools\arena\package-arena.mjs --addon "$cs\game\csgo_addons\cs2sm_stadium_v1" --map soccer_2v2_arena --models models/soccermod_gym --out <2v2.vpk>
```

Copy `resource/overviews/soccer_2v2_arena.txt` into the game addon folder before packaging.

## Known limits

- The ball and the players have no shadow on the floor (tried: every material without shadows -
  the sky light then floods the hall and there is still no shadow).
- No reflection cube map is built by the `-fshallow` compile.

## Brand boards (test only)

`tools/brands/` puts adverts with real brand names in front of this map's kick boards and two cloth
banners on its east wall - as overlay models of an unlisted test item (3811741091) that only the
test server mounts. The map itself is unchanged.
