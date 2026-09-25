# Changelog

All notable changes to CS2 SoccerMod are documented here. See
`docs/releases/` for the long-form notes behind each entry.

## [Unreleased]

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
- Ball size (`!menu` → Admin → Ball → Ball size): Default 37.6 u, 10%, 12.5%, 15%
  or 20% smaller (about the CS:S ball). The engine's SetScale input
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
