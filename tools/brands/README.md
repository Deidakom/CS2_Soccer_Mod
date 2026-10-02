# brands — advert boards with real brand names

Owner 2026-10-02: "can we add real brands as well, such as Coca Cola, Nike, Adidas ... mix them in",
first for the test server, then "put that on all maps and servers" - inside the stadium's Workshop
item (his choice). The brand NAMES are drawn here in each brand's colours and a similar lettering
(fonts that are on every Windows PC) - not the companies' logo drawings. They are real trademarks:
if an item is ever objected to, take the `soccermod_brands` folders out of it - the plugin then
falls back to the old boards by itself.

Where they are: `models/soccermod_brands` + `materials/soccermod_brands` in the stadium item
3811382872 (from update 6), which every server mounts. The plugin (`SoccerModMvpPlugin.BrandBoards.cs`,
script `plugin/apply_brand_boards.py`) switches when it finds the models among the mounted Workshop
items:

| where | what |
| --- | --- |
| stadium (LED boards round the pitch) | `led_board.vmdl`: 16 pages instead of 10 (12 brands, six of them real), every brand board shows another brand |
| soccer_indoor_hall | `hall_boards.vmdl`: LED adverts 0.6 in front of the boards; each run between the goals is filled with whole adverts (19 of 179 units) |
| soccer_2v2_arena | `2v2_boards.vmdl`: a printed advert on every kick-board panel; `2v2_banners.vmdl`: two cloth banners on the east wall |

The overlays are models in world coordinates, spawned at the origin; the maps themselves are
unchanged. An unlisted test item, 3811741091, holds the same files (it was the first test path).

## Build

```powershell
$a = "<content>\csgo_addons\cs2sm_stadium_v1"
powershell -ExecutionPolicy Bypass -File tools\atmo\render-board-pages.ps1 <pages dir>
powershell -ExecutionPolicy Bypass -File tools\brands\render-brand-pages.ps1 <pages dir>
node tools\brands\generate-brand-overlays.mjs $a <pages dir> [<hall preview dir> <2v2 preview dir>]
node tools\atmo\generate-led-boards.mjs $a <pages dir> --pages kickfuel,cocacola,voltwave,nike,goalcrest,soccermod,adidas,topcorner,puma,pitchline,pepsi,emirates,fairplay,respect,goal_red,goal_blue --model models/soccermod_brands/led_board --mats materials/soccermod_brands/boards
resourcecompiler -r  ...\materials\soccermod_brands\*.vmat ; ...\models\soccermod_brands\*.vmdl
```

Into the stadium item: copy the compiled `models/soccermod_brands` and `materials/soccermod_brands`
into a folder and merge it over the item's package (`tools/arena/vpk_tool.py merge <out> <item vpk> <folder>`).

The page order of the LED board is fixed in the plugin (AtmoBoards.cs): 0-11 brands with the
SoccerMod banner at 5, 12-13 texts, 14 / 15 goal red / blue; + 16 = 70 % lit, + 32 = 30 %, 48 = dark.
