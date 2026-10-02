# Changelog

All notable changes to CS2 SoccerMod are documented here. See
`docs/releases/` for the long-form notes behind each entry.

## [Unreleased]

- 2026-10-02 (live): new map `soccer_2v2_arena` (Workshop 3811585232), a small hall for two
  against two: parquet court 1040 x 1440, a cage on the long sides (kick boards, steel mesh,
  nets), the end walls in play with the 2v2 goal (156 x 62) set into them, fans behind the
  cage. Map profile `Gym`; the hall rules apply there (the map's fans cheer, no stadium
  sounds, no celebrations; no 3D grass or pitch designs on the parquet). Tools: `tools/gym/`.
- 2026-10-02 (live): new map names. The stadium is `soccer_soccermod_arena` again (Workshop
  updates 3 and 4 carried it as `ka_soccermod_stadium`), the indoor hall is
  `soccer_indoor_hall` (was `ka_soccermod_indoor`). Same Workshop items; the plugin answers
  to the old names too, so a server on an older revision keeps working. A server needs
  `cfg/maps/<new name>.cfg` next to the old one.
- 2026-10-01 (live): the stadium effects run on the indoor hall too - its own fans cheer, wave
  and celebrate goals, the goal show plays in hall positions (no fireworks under the roof),
  crowd sounds and announcer; four pitch designs and 3D grass for the hall (own models in
  its Workshop item). Hall map update 2: ball, players and fans are lit, two missing turf
  bands fixed.
- 2026-10-01 (live): new map `ka_soccermod_indoor` (Workshop 3811545272), our own indoor
  hall for 3 to 4 players per team: boarded court with rounded corners, nets up to a
  ceiling net, goals set into the end boards, two stands, terraces, 1,676 fans, video
  cube. In the default map pool as an optional map; map profile `Hall`. Tools:
  `tools/hall/`. Texture recipes shared with the stadium (`tools/arena/lib/recipes.mjs`).
- 2026-10-01 (live): the SoccerMod stadium `ka_soccermod_stadium` (Workshop
  3811382872) is the default map: our own round two-tier stadium around the
  unchanged v8 pitch, goals at the CS:S positions, crowd in every block, LED
  ring under the roof, sharper grass. The same Workshop item now carries
  everything the plugin shows and plays (the former Feature Package
  3797479770), so servers and players need this one item only
  (`mm_extra_addons "3811382872"`). A fresh server switches to this map;
  the default map pool is the stadium and the indoor hall (2026-10-02: Soccer_Multi_Indoor is out of the pool). The v8 map
  still works when an admin adds it. Tools: `tools/arena/`.
- Own goals are celebrated by the team that gets the goal (team goal show,
  crowd roar, then whistles).
- 3D grass (lit tiles): white blades over the centre circle, penalty arcs
  and spots no longer render black.

## [1.5.0] - 2026-09-26

- Drop-in releases: Linux and Windows (beta) ZIPs with Metamod 1469,
  CounterStrikeSharp v1.0.375 (with runtime), MultiAddonManager v1.6.1 and
  the configs; `install.sh` / `install.bat` add the gameinfo.gi line. A fresh
  server switches to the stadium by itself (`css_sm2_automap off`); no
  spin commands without the native bridge. Windows native bridge (MSVC, CI).
- Soccer TAB board over CS2's scoreboard (position, captain, goals,
  assists, saves, score, clock); compact top scoreboard by default (Full
  with team names in !menu → Settings → Match), attached status line;
  Settings grouped into Match / Sprint / Visuals / Sounds / Menu.
- 3D grass: 320 tiles, cut-out blades, on for everyone.
- Kick reach 64 / aim cone 40 by default.
- 2026-09-26 (live): 3D grass on SoccerMod-sized pitches (80 tiles in the
  Workshop package, per-player toggle in !menu → Settings → 3D grass, on
  by default; admin `css_sm2grass`); Panorama match scoreboard, bigger and
  covering the CS2 top bar; simple ball menu with kick reach/cone dials
  (advanced view kept); sprint chat messages off by default; a goal leaves
  the ball in the net until the round restart and no longer kills the
  conceding team; net sound on every goal; kicks on the frozen kickoff
  ball keep full speed and walking up to it no longer makes it roll;
  portable native bridge (glibc 2.31) and example configs for releases.
- New defaults = the tuning the test server plays with (2026-09-24): kick
  reach 70 and aim cone 50 ("Middle" preset), right click 0.4 (crouched
  0.7), elevation sensitivity 0.55, crouch lift 0, spin factor 0.1 (little
  curve), clickable menu on, public access 1, HTML fallback menu.
- Clickable, bind-free menu (`!menu` or B; `!menumouse off` for keys only),
  shipped in Workshop item 3797479770 together with the jerseys. `!bind`
  and `!links` print binds and the Workshop link to the console.
- Ball: realistic ground bounce, long balls roll on, knifing a fast
  incoming ball takes its speed out, no backspin on balls knifed back,
  knife duels (first clean kick wins), softer body push without a
  steamroller, kick-cone presets, cannon default power 1.
- Ball size (`!menu` → Admin → Ball → Ball size): CS2 Legacy (the full
  37.6 u map ball), 10%, 13% (the new default, 32.7 u), 15% or 20%
  smaller (about the CS:S ball). The engine's SetScale input
  resizes the live ball's physics and look together (measured with the
  console probe `css_sm2ball_sizetest`); reach, cone edge, lift, push, goal
  line and GK boxes follow the radius. Saved, and part of presets and undo.
- Removed Advanced Training and Shot Drills / Replay (also listed as
  Training Settings) from the menus; `css_ball_target`/`css_ball_replay`
  still work.
- Fixed: the plugin failed to load on a cold server start (ELO read the
  player list before the engine globals existed).
- Ball realism ([analysis](docs/ball-realism-analysis-2026-09-24.md)): the
  landing limiter no longer clips the ground bounce; new Ball menu dials
  "Rolling resistance" (speed lost per second by a slow roll, realistic
  50-100) and "Curve in flight" (side spin bends the ball, 1 = real ball).
  Both are 0 (off) by default.
- Jersey ragdolls collapse again: the kit models get the stock agent's
  ragdoll joints ([details](docs/jerseys/2026-09-24-ragdoll-joints.md),
  `tools/resource-blocks`). Published in 3797479770 together with the
  Back | Page | Next menu layout.
- Enemy names over their heads; the kickoff ground line on the grass; no
  leaving the pitch through the halfway railing gaps.

- ELO ranking after Subear-17's SoMoE plugin (reimplemented, credited in
  `SoccerModMvpPlugin.Elo.cs`): 5v5/6v6 matches that reach full time are
  rated (K=32, team-average ELO plus an MVP-points nudge of up to ±16);
  the lower-rated captain picks first when captains differ by more than 6%;
  a halftime rebalance vote at a 5+ goal gap; `!elo` with ranked/unranked
  leaderboards, player cards, name history and admin rename/adjust.
  Settings: `css_sm2elo_config firstpickgap|swapgap`. Data: `soccermod_elo.json`.
- Fixed: scores and match team names now follow the squads across the
  halftime side swap. Second-half goals used to be added to the other
  squad's score.
- Removed `!tp`: other players kept seeing its camera as a floating ERROR
  model after three hiding attempts.
- Knife world models are invisible, so other players no longer see a knife
  standing in the grass under each player.
- The kickoff ground line lies on the grass instead of floating ~26u up.
- Players can no longer leave the pitch through the railing openings at the
  halfway line.
- `!links` prints the Workshop link to the console, where it can be copied.

Ball handling and menu optimization, see
[docs/ball-and-menu-optimization-2026-09-24.md](docs/ball-and-menu-optimization-2026-09-24.md):
lag-compensated knife contact (default 100 ms, `0` = off), fair
simultaneous body pushes in every handling profile, escaped names and page
memory in the menu, Help → Ball controls, and less per-swing log volume.

CS2 1.41.8.2 support, see
[docs/cs2-1.41.8.2-server-stack-2026-09-24.md](docs/cs2-1.41.8.2-server-stack-2026-09-24.md):
the server now needs CounterStrikeSharp v1.0.375 on KHook Metamod (build
1467 or newer). The native bridge is rebuilt for KHook and takes its
`AcceptInput` signature from CounterStrikeSharp's gamedata (unique matches
only). `deploy/testserver/update-server.sh` updates CS2 and installs the
tested Metamod 1469 + CounterStrikeSharp v1.0.375 pair with backup, gameinfo
repair, verification and rollback. It also moves an installed
MultiAddonManager to v1.6.1: v1.5.4's stale offsets made joining players fail
with "Required map is missing on your client".
- Removed `!tpf` (front-facing third-person camera) at the user's request
  after two fix attempts still didn't render the front correctly. `!tp`
  (the normal rear third-person camera) is unaffected.
- Goal announcements now use the persistent jersey-side labels `(Home)` and
  `(Away)`, including after halftime team swaps.
- Added the [jersey installation tutorial](docs/jerseys/INSTALLATION.md),
  covering Workshop delivery, static kit activation, dynamic-jersey state,
  client verification and rollback.
- Restored the previously approved belt/waist geometry in the jersey Workshop
  package while retaining the current numbers, labels, logos, colors and
  goalkeeper styling.

## [1.0-Beta-Official] - 2026-09-05

See [docs/releases/v1.0-Beta-Official.md](docs/releases/v1.0-Beta-Official.md).

## [1.1.0] - 2026-09-01

See [docs/releases/v1.1.0.md](docs/releases/v1.1.0.md).

## [1.0-beta.3] - 2026-08-31

Replaced the large native sprint-cooldown indicator with a subtle
ten-segment bar; hidden while the SoccerMod menu is open.

## [1.0-beta.2] - 2026-08-31

See [docs/releases/v1.0-beta.2.md](docs/releases/v1.0-beta.2.md).

## [1.0-beta.1] - 2026-08-31

See [docs/releases/v1.0-beta.1.md](docs/releases/v1.0-beta.1.md).

## [1.0-beta] - 2026-08-30

First public beta. See [docs/releases/v1.0-beta.md](docs/releases/v1.0-beta.md).
