# Handover: XSL-style stadium on the map-test server (2026-09-26)

Scope: **only** the new stadium map on the map-test server. Everything else
(plugin, Feature Package, main server, Moscow, releases) is out of scope for
this session.

## Goal (owner)

Rebuild the structure of the CS:GO map **soccer_xsl_stadium** (Workshop
1476746113) around our own pitch: stands, roof, trusses, walls, glass.

- **Own files only.** Nothing from the XSL map is shipped. The geometry is
  re-created from its BSP by a tool; the textures are generated look-alikes or
  CS2 stock materials.
- **Day only** for now. Night, floodlights and day/night switching are later.
- The owner said Valve materials do not fit, so the XSL materials got own
  generated textures matched to the originals.
  - Comparison image: `.local/refmaps/xsl-textures-original-vs-ours.png`.

## Where things are

| What | Where |
|---|---|
| Map source (converted) | `E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\content\csgo_addons\cs2sm_stadium_v1\maps\soccer_soccermod_stadium.vmap` |
| Backup: our map before XSL | same folder, `soccer_soccermod_stadium.vmap.bak-before-xsl` (= `.bak-before-grass`, byte-identical) |
| Compiled map | `...\game\csgo_addons\cs2sm_stadium_v1\maps\soccer_soccermod_stadium.vpk` |
| XSL reference BSP (read-only input) | `<repo>\.local\refmaps\soccer_xsl_stadium.bsp` (git-ignored) |
| XSL entity dump, CS2 material list, our mesh list | `<repo>\.local\refmaps\xsl_entities.json`, `cs2_materials.txt`, `stadium-meshes.json` |
| Generated XSL textures + vmats | `content\csgo_addons\cs2sm_stadium_v1\materials\soccermod_xsl\` |
| XSL structure models (4 quadrants) | `content\csgo_addons\cs2sm_stadium_v1\models\soccermod_xsl\xsl_q0..3.vmdl` + `.dmx` |
| Workshop item (test map) | **3807839299**, live manifest **30370650408375125** (uploaded 13:27 UTC) |
| Package on the VPS | `/home/gameserver/xslmap-upload/3807839299.vpk` (+ build dir `/root/xslmap-pkg/`) |
| Publish file | `/home/gameserver/xslmap-update.vdf` |
| Test server | `cs2-soccermod-maptest` (port 27018, overlay over the main install; README in `deploy/testserver/maptest/`) |

## Tools (committed, `tools/xsl/`)

1. **Textures:** `node tools/xsl/generate-xsl-textures.mjs`
   - Writes `<name>_color.png` (plus `_trans.png` for glass and truss) and one `csgo_complex` `.vmat` per texture.
   - 13 materials: `metal_grey/red/blue/greygreen`, `blackmetal`, `grey_wall`, `sch` (seats), `vitre_sale`, `ceiling`, `glass_clear` (translucent), `metal_railing`, `truss` (alpha test), `tilefloor`.
2. **Structure:** `node tools/xsl/extract-xsl-structure.mjs --bsp .local/refmaps/soccer_xsl_stadium.bsp --addon "<content>\cs2sm_stadium_v1"`
   - Reads BSP lumps 0/2/3/6/7/12/13/14/43/44: the world plus visible brush entities.
   - Drops tools, sky and logo faces and everything inside our pitch box (`|x|≤1300`, `|y|≤1700`, below z = −32 + 150).
   - Shifts by `(+45, −13, +869)` onto our pitch.
   - Maps materials through the `MATERIALS` table.
   - Writes 4 quadrant models (render mesh + `PhysicsMeshFile`): about 6,300 faces, 31k vertices.
   - `--report 1` only prints the statistics and writes **nothing**. Don't be fooled by that.
3. **Map:** `node --max-old-space-size=6144 tools/xsl/apply-xsl-vmap.mjs --in <…bak-before-xsl> --out <…soccer_soccermod_stadium.vmap>`
   - Removes every **world** `CMapMesh` whose bounding sphere lies completely outside the pitch box. That was 2,147 meshes: seats, concrete, roof, LED boards.
   - Also removes `func_brush`/`func_wall`/`func_reflective_glass` entities fully outside the box. That was 0 this time.
   - Places the 4 XSL models as `prop_static` at the origin, copied from the existing `pitch_line_legacy` prop_static.
   - Always start from `.bak-before-xsl`.
   - Flags: `--dry-run 1` (report only); `--no-remove 1` and `--no-props 1` for bisecting.

### KV2 vmap facts learned the hard way

- **Top-level elements** (e.g. the template prop_static):
  - have **no commas** between them;
  - are listed by id in the world's `children` array and in editor selection sets (`"element" "<uuid>",`).
  - Adding or removing one means editing those id lists too. The tool does both.
- Nested elements inside `element_array`s **are** comma-separated.
- The file mixes CRLF and LF. Read and write it as UTF-8 text; a no-op round trip is byte-identical.
- The compiler's only error for any mistake is `Failed to load map "…"` within one second. Bisect with the flags above.
- `Failed loading resource "scripts/detail_prop_types.vdata_c"` is harmless (it also appears for the original map).

## Build → package → publish → test

1. **Compile** the textures, models and map with Workshop Tools' `resourcecompiler.exe` (under `…\game\bin\win64\`):
   - Materials: `-nop4 -f -game "<CS2>\game\csgo" -i "<addon>\materials\soccermod_xsl\*.vmat"`
   - Models: `-nop4 -f -game "<CS2>\game\csgo" -i "<addon>\models\soccermod_xsl\*.vmdl"`
   - Map: `-nop4 -game "<CS2>\game\csgo" -addon cs2sm_stadium_v1 -fshallow -i "<addon>\maps\soccer_soccermod_stadium.vmap"`. It takes about **24 minutes**; last result: `OK: 180 compiled, 0 failed`.
2. **Package** on the VPS:
   - Tar `maps/soccer_soccermod_stadium.vpk`, `materials/soccermod_xsl`, `models/soccermod_xsl` from `…\game\csgo_addons\cs2sm_stadium_v1`.
   - Base = the live item content, `/home/gameserver/cs2-maptest-data/upper/game/bin/linuxsteamrt64/steamapps/workshop/content/730/3807839299/3807839299.vpk`.
   - List the base entries with a small python VPK-v2 parser (tree starts at byte 28; each entry is 18 bytes plus preload). Several copies of it are in this session's transcript.
   - Merge with `/root/vpk-merge`: it has hardcoded inputs `nm/` and `nm-base-entries.txt`. Move them aside, put yours in, run `DOTNET_ROOT=$D $D/dotnet bin/Release/net10.0/merge.dll <base> <out>` with `D=/home/gameserver/cs2/game/csgo/addons/counterstrikesharp/dotnet`, then restore them.
   - Last result: replaced 1, added 43, kept 518 entries.
3. **Publish** as the gameserver user:
   ```text
   runuser -u gameserver -- /home/gameserver/steamcmd/steamcmd.sh +login s5dgtgcb +workshop_build_item /home/gameserver/xslmap-update.vdf +quit
   ```
   **The owner approves every Workshop upload.**
4. **Delivery:**
   - Steam delivers in 20–90 minutes.
   - The test server only fetches a new map version when it loads the map. While nobody is on it, reload with RCON `host_workshop_map 3807839299` (every 5 minutes until `appworkshop_730.acf` in the overlay shows the new manifest). Use `python3 deploy/testserver/serverctl.py rcon 127.0.0.1 27018 /etc/cs2-soccermod-maptest.env "<cmd>"` from `/root/cs2-soccermod-src`.
   - The owner restarts CS2 to get the new version.
   - Server restarts only when needed and when the owner allows them.

## Current state and known issues

- **Live on 27018 since 13:57 UTC.** The plugin loads and finds the ball.
- The owner has **not yet given feedback** on the look. Start by asking for screenshots.
- **Old geometry kept at the pitch edge:** about 390 of our old meshes survive, because their bounding sphere touches the pitch box (the conservative test). Expect overlaps and old concrete and seat pieces at the edges. Options:
  - a per-vertex bounds test instead of spheres;
  - a z-limit;
  - a hand-made keep/remove list, using `.local/refmaps/stadium-meshes.json` and `tools/inspect-vmap-meshes.mjs`.
- **Physics:** the map build warned `ExtrudeReplaceHull failed … Failed to create physics hulls for one or more meshes`, so some XSL parts may not be solid. Check in game (walk into stands and walls, kick the ball onto the roof).
- **Not ported yet:**
  - XSL's 62 `point_spotlight` / 66 `light_spot` floodlights, dome lights and sky sphere;
  - day/night logic;
  - XSL's own goals (ours stay; measured geometry: posts |x| = 127, crossbar 102, goal lines ±1380).
  - The lighting is still our map's own sun and sky.
- **Roof scoreboard:** the 28 `Counter_digit_*` func_brush digits were kept (entities are not removed). Since today the plugin drives them (`MapScore.cs`, the map's math_counters). With our old roof gone they may now hang at their old spot without the structure around them. Check and move or hide them if needed.
- **Must not change** (the plugin relies on them):
  - pitch ±1280 × ±1664 at floor z = −32;
  - walls, goals and nets, goal lines ±1380;
  - `IsFoundationMap` includes `soccer_soccermod_stadium`.
  - The grass pitch check and goal measurement must still pass: look for `grass_pitch_check … compatible=True` in `journalctl -u cs2-soccermod-maptest`.
- **Earlier trial:** the 50 grass-trial tiles were removed from this map. The new 3D grass comes from the plugin and the Feature Package, not the map.

## Rules that apply (owner)

- Own files only; day only.
- The owner approves every Workshop publish.
- No unprompted server restarts. The map-test server is the place to test; the main server (27017) stays on its map.
- The Moscow server (myarena) is not part of this work.
- Player settings go in `!menu`, not new chat commands.
