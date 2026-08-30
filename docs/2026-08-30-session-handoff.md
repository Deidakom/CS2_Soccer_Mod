# Session handoff — 2026-08-30

Self-contained. A fresh session can pick this up with no prior context.

## 0. Read this first — honest status

A large amount was built today and **deployed**, but the user's verdict at
end of session was: *"none of the stuff you implemented works."*

The gap is a **verification gap**, and it is the single most important thing
to fix in how the next session works:

- Almost everything today was verified **only** via RCON commands and
  `[SM2DIAG]` server logs. That proves server-side code paths execute.
- It does **not** prove the feature works for a player: HUD rendering,
  keybinds, menus, visuals, and "feel" are all client-side and were never
  checked in-game by the agent.
- Several things were also shipped on **unverified assumptions about the
  CSSharp API** and had to be redone 2-3 times (the menu, three times).

**Next session: start with an in-game verification pass with the user, one
feature at a time, before writing any new code.** Do not add features while
the basics are unconfirmed.

## 1. Environment

- Repo: `C:\Users\sergi\Documents\ChatGPT\Privat\cs2-soccermod`
- Plugin: `src/server-plugin/SoccerModMvp/` — `partial class
  SoccerModMvpPlugin`, assembly `SoccerModNativeHull`, namespace
  `SoccerModMvp`, CSSharp **1.0.373**, net10.0, version string `4.0.0-alpha1`.
  Files: `.cs` (ball core), `.Admin.cs`, `.Config.cs`, `.Match.cs`,
  `.Cap.cs`, `.Sprint.cs`, `.Menu.cs`, `.Social.cs`, `.MapCleanup.cs`.
- Build:
  ```
  DOTNET_CLI_HOME=C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\dotnet-home
  NUGET_PACKAGES=C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\nuget-packages
  C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\dotnet-sdk\dotnet.exe build
    src/server-plugin/SoccerModMvp/SoccerModMvp.csproj -c Release
  ```
- Deploy: `SOCCERMOD_HOST=cs2-soccermod bash deploy/testserver/push-ball-build.sh`
  (restarts the service).
- RCON: `ssh cs2-soccermod '/root/rcon "<cmd>"'`
- Logs: `ssh cs2-soccermod "journalctl -u cs2-soccermod-test.service --since '5 min ago' --no-pager | grep SM2DIAG"`
- **CS2 test server connect:** `password <sv_password>; connect 212.87.212.58:27017`
  (port 27017 — 27015 is the separate, still-running CS:S reference server).
- CS:S reference server: same VPS, `cssserver.service`, port 27015. Probe
  plugin `soccermod_css_ball_probe` is installed; reload after a CS:S
  restart with `/root/css_rcon "sm plugins load soccermod_css_ball_probe"`.
  RCON helper: `ssh cs2-soccermod '/root/css_rcon "<cmd>"'`.

Config files (server-side, under the plugin's ModuleDirectory):
`soccermod_admins.json`, `soccermod_bans.json`, `soccermod_settings.json`
(ball tuning), `soccermod_match_settings.json`, `soccermod_last_match.txt`.

## 2. Evidence table — what is actually proven

| Feature | Evidence | Status |
|---|---|---|
| Sky-path platform removal | `sky_path_platform_removed` x2; vertical trace from centre now `hit=False` for 4096u (was blocked) | **Proven server-side** |
| Soft pass (aim low = softer) | Live kicks logged `softPassScale=0.77`, `0.96` | **Proven server-side**, feel unrated |
| Team scoreboard write | `team_scoreboard_stamped teams=4 scoreCt=1`, survives kickoff restart | **Proven server-side**, not seen on a real Tab scoreboard |
| Goal detection | `goal_scored` both ends, correct team, no double-count | **Proven** |
| Kickoff wall | `kickoff_wall_start`, `kickoff_wall_cleared reason=timeout` | Proven to arm/clear; **never tested with 2 real players** |
| Ball push | 9 × `ball_push_start` during user's session | Fires; strength unrated |
| Sprint | 9 × `sprint_start` | Fires; unrated |
| Wall-pop trick kick | 1 × `kick_wall_pop` | Fired once |
| Menu **input** | `menu_key source=command number=1 hasOpenMenu=True` | **Input reaches the server via `css_1` binds** |
| Menu **display** | none | **UNVERIFIED — prime suspect** |
| Password | `sv_password` set, persisted in cfg | **Proven** |
| Settings persistence | JSON files verified on disk, survive restart | **Proven** |

## 3. The menu — full history, do not repeat these

Three implementations were shipped; the user rejected the first two.

1. `ChatMenu` — requires typing `!1`/`!2`. Rejected: "I want number keys."
2. `CenterHtmlMenu` — scrolls with W/S/E, and its instance owns an **OnTick
   listener so it redraws every single tick** → the flicker the user saw.
   Rejected.
3. Current: own `NumberMenu` type in `.Menu.cs` — `PrintToCenterHtml` panel
   redrawn on a slow loop from `MenuOnTick` (default 1.0s, tunable
   `css_sm2menu_hud <seconds>`), input via number keys.

**Key facts established (do not re-derive, do not contradict):**
- `bind F10 "css_menu"` **works**. Plugin `css_*` commands ARE bindable.
  (An earlier claim that they aren't, due to `FCVAR_CLIENT_CAN_EXECUTE`,
  was never verified and is **wrong** — do not repeat it.)
- `slot1`..`slot9` command listeners **never fire** on this server (0 events
  logged, ever). Likely because a knife-only loadout has nothing in those
  weapon slots. Don't rely on that path.
- `css_1`..`css_9` commands **do** reach the server when bound. Confirmed:
  `menu_key source=command number=1 slot=0 hasOpenMenu=True`.
- Required client binds (user must have these):
  ```
  bind F10 "css_menu"
  bind 1 "css_1"  ... bind 9 "css_9"
  ```

**So the most likely remaining fault is the panel not being visible.**
Debug `PrintToCenterHtml` first: check whether it renders at all, whether
the 1.0s redraw is too slow (panel fades between redraws) or still too
fast (flicker), and whether the HTML class names used
(`fontSize-m`/`fontSize-sm`/`fontSize-s`) are valid in this CS2 build.
Consider testing with a plain `PrintToCenter` string first to establish a
baseline that *something* shows.

## 4. Everything changed today (all deployed)

**Ball / feel**
- Kick velocity 1440 → 1555 → **1602 u/s**.
- Aim cone widened 55° → **70°** (`KickMinimumAimDot`), after log analysis
  showed silent `outside_aim_cone` rejects were what felt like "input lag".
- Kick cooldown 0.35 → **0.48s**, measured from two consecutive real knife
  hits on the CS:S server (that is the vanilla knife swing rate).
- Overhead/volley bonus added then reduced: 0.5 → 0.15 → **0.14**.
- Body push: added (native contact imparts nothing), then +20%
  (`BallPushTransferRatio` 0.84, `BallPushMaxSpeed` 264), then a
  **kickstart floor** so the first push on a dead ball isn't harder than
  pushing a rolling one.
- **Soft pass**: aiming below ball centre scales power down (full until
  0.35 radii below, → 0.25× at 1.60). `css_sm2ball_softpass <start> <full> <minScale>`.
- Wall-pop trick kick: look down ≥25° at a ball trapped against a wall →
  30% chance to pop it up straight/left/right.
- Ball spawns at true pitch centre (0,0) — the old code copied the CSF
  map's own off-centre ball placement.
- Ball rebuild now **synchronous** at round start (`round_start_immediate`)
  and the map's original ball is hidden via `OnCheckTransmit`, to kill the
  visible spawn-in pop.
- Post-goal centre reset **removed** (kickoff restart already does it —
  it was resetting twice per goal).

**Match / gameplay**
- Golden goal, readycheck (`!rdy`), forfeit vote (`!forfeit`), team names
  (`!teamname`), live hostname status, match log file, MVP announcement.
- Kickoff wall/possession with 10s timeout, cleared by first touch
  (kick or push) from the kicking team.
- Own-goal attribution by last **toucher** (push counts, not just kick).
- Real CS2 team scoreboard written + re-stamped after every kickoff
  (`mp_restartgame` zeroes it).
- Goal calibration + match config now **persisted**.

**Social / admin**
- `!spec me` public, `!pos`, `!lc`/`!late`, `!help`/`!commands`.
- Own admin/ban system, `sv_password` set locally (not committed).

**Map**
- Sky-path buttons + teleports killed (12 entities).
- **Sky-path platform**: two unnamed pitch-spanning `func_brush` slabs at
  z 896–961 removed (matched by shape, not name). This was the "ball stops
  in mid-air" cause and was missed the first time.

## 5. Dead ends — do not retry

- **Landing/jump sound cannot be removed.** No sound or user-message
  listener exists in CSSharp 1.0.373 (full listener list enumerated by
  reflection). No `sv_land_sound` / `sv_landing_sound` / `player_land_sound`
  / `sv_jump_sound` cvar exists in this CS2 build. Only `sv_footsteps 0`,
  which kills all footstep audio. **User's decision: leave the sound.**
- **Rate cvars**: `sv_mincmdrate`/`sv_maxcmdrate`/`sv_minupdaterate`/
  `sv_maxupdaterate` do not exist in CS2. `sv_minrate`/`sv_maxrate`/
  `sv_lan`/`sv_hibernate_when_empty` are `DISALLOWED WORKSHOP CONVAR` on a
  `host_workshop_map` server. `sv_password` is fine.
- **Ball spin / curve**: blocked on CS2Fixes publishing an updated
  `CEntityInstance_AcceptInput` signature (still 2026-08-24 as of today;
  server binary is 2026-08-28). Native plugin is built and loads; the write
  path is inert until then. Agent is **hard-blocked by the safety classifier
  from running `meta load`** — the user must run it.
- **Air/gravity is already correct** — do not tune it. Measured on both
  servers with a mirrored flight trial: apex/hangtime/range match CS:S at
  10.6° and 35°, and both undershoot the no-drag prediction identically.
  The "drops like a stone" feel is the missing **spin**, not gravity.

## 6. Suggested next steps

1. **In-game verification pass with the user, feature by feature.** Nothing
   new until the list in §2 has real in-game verdicts. Fastest order:
   menu display → number keys → push feel → sprint → soft pass → scoreboard.
2. **Fix the menu panel** (§3) — most likely a `PrintToCenterHtml`
   rendering/timing issue. Start from a plain `PrintToCenter` baseline.
3. Only then: remaining Tier 2 (serverlock/AFK, deadchat) and Tier 3
   (training, GK areas, stats, skins — skins/shouts need a workshop-addon
   pipeline spike first).
4. Standing weekly check for the CS2Fixes signature (unblocks spin, which
   is the biggest remaining "feel" item).

## 7. Non-negotiables (unchanged)

Knife left-click only; body push must work; ball must never damage players;
native VPhysics only (never reintroduce the v2 analytic position
controller); match/CAP work pauses whenever ball feel regresses.
