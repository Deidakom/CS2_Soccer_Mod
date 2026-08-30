# Plan: goal round-win punishment + MultiAddonManager (2026-08-30)

User-approved planning only - NOT implemented yet. Server is in active use;
no builds/deploys until the user gives the go. Expands items from
`2026-08-30-community-repos-plan.md` (its section C is superseded by Task 2
here).

## API facts verified by reflection against CounterStrikeSharp 1.0.373 (this session)

- `CCSPlayerController.CommitSuicide(bool explode, bool force)` EXISTS (also
  on CCSPlayerPawn / CBasePlayerPawn).
- `CCSGameRules` has NO TerminateRound *method* (only schema props like
  `RoundEndReason` getters).
- BUT `VirtualFunctions.TerminateRound` EXISTS: a signature-based
  `MemoryFunctionVoid` with platform variants -
  Linux: `(IntPtr gameRules, RoundEndReason reason, float delay, IntPtr, byte)`
  (Windows variant has delay/reason swapped). `RoundEndReason.CTsWin` /
  `TerroristsWin` exist. Signature ships with CSSharp itself (not CS2Fixes),
  and the rest of 1.0.373's sig-based features work on this server binary
  (2026-08-28) - decent odds it works, but it MUST be verified in-game via
  `css_sm2goal_test` before anything depends on it.
- Game rules pointer: `CCSGameRulesProxy.GameRules` (find proxy via
  `Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")`).

## Task 1 - Goal scored: conceding team dies (+ optional native round win)

Core principle: the existing goal flow (detection, score, announce,
GoalPause, kickoff restart, scoreboard stamping) stays UNTOUCHED. Both new
behaviours are additive and individually toggleable.

### 1a. Punishment kill (the user's ask - guaranteed-working mechanism)
In `OnGoalScored` (Match.cs), after the announce:
- Conceding team = the team whose goal the ball entered (defending side),
  independent of last toucher - own goals still kill the conceding side.
- For every valid, alive, non-bot-exempt player on that team:
  `player.CommitSuicide(false, true)`.
- `mp_respawn_on_death_t/ct 1` is already set, so they respawn in seconds,
  and the kickoff restart re-teleports everyone anyway - pure flavor, no
  flow change. `mp_autokick 0` is already set, so no suicide kicks (the
  old ct_killer suicide-ban history is why this matters).
- Golden goal: punish first, then the Finished transition as today.
- Accepted cosmetic: each conceded goal adds +1 to those players' death
  count on the Tab scoreboard. Note it to the user; only fight it if asked.
- Toggle `css_sm2goal_punish <on|off>`, persisted in MatchSettingsStore,
  default ON (user requested the feature).
- Log `[SM2DIAG] goal_punish team=... killed=N`.

### 1b. Native round award via TerminateRound (nice-to-have, verify-first)
Gives CS2's own "Counter-Terrorists Win" banner/music on each goal.
- Call `VirtualFunctions.TerminateRound.Invoke(gameRulesPtr, reason,
  delaySeconds, 0, 0)` with reason CTsWin/TerroristsWin and delay = the
  existing GoalPause length; use the LINUX argument order (server is Linux).
- Phase machine: on goal with roundwin enabled, skip the GoalPause's
  pending `mp_restartgame` - the round end itself restarts the round; the
  existing `OnRoundStart` path (ball rebuild, scoreboard re-stamp, kickoff
  wall) already handles everything from there. GoalPause -> Live transition
  moves to MatchOnRoundStart (transition may partially exist - check).
- REQUIRED cfg guards in `gamemode_casual_server.cfg` BEFORE enabling:
  `mp_maxrounds 0` and `mp_halftime 0` - once real round wins happen, CS2's
  own match flow (halftime team swap, match end at maxrounds) would
  otherwise fight our match logic. Also confirm `mp_ignore_round_win_conditions 1`
  does not block a direct TerminateRound call (test will show it).
- Native team score will increment on round wins; harmless -
  MatchOnRoundStart re-stamps our authoritative score right after.
- Toggle `css_sm2goal_roundwin <on|off>`, persisted, default OFF until the
  in-game test passes (unverified signature = same class of risk as every
  sig-based call; if it silently no-ops or crashes, punish-only mode is the
  fallback and the toggle stays off).
- Verify: `css_sm2goal_test t` / `ct` from RCON with the user watching:
  banner shows, correct team credited, round restarts once (no double
  restart), ball at centre, no `status=139` in journal.

Effort: S (1a) + M (1b). Files: Match.cs, Config.cs (two settings),
gamemode_casual_server.cfg, main file only if the gamerules-proxy helper
doesn't exist yet.

## Task 2 - MultiAddonManager (transport layer for all Tier 3 content)

What it is: Source2ZE's Metamod plugin; mounts extra workshop addons
server-side and makes clients download them alongside the
`host_workshop_map` map. Unlocks: custom kick/bounce sounds, shouts,
jerseys/skins, and distribution of a patched stadium map (the roof
scoreboard PVS fix + CSF logo removal both live there).

Steps:
1. **Pre-check (agent, read-only, do first):** latest MAM release date and
   its gamedata vs server binary 2026-08-28; scan open issues for breakage
   on the current CS2 update. MAM is signature-based - the CS2Fixes stale-
   signature trap applies. If stale: STOP and wait, exactly like spin.
2. **Stage (agent):** download the Linux release, stage into
   `/home/gameserver/cs2/game/csgo/addons/` (it ships
   `addons/multiaddonmanager/` + a metamod .vdf), plus its cfg with a tiny
   known-good test addon id in `mm_extra_addons`. Nothing loads until
   restart.
3. **Load (USER):** loading native code into the live server is the user's
   action by standing rule - either they restart the service at an agreed
   empty-server moment, or run `meta load` themselves. Agent never runs
   meta load.
4. **Verify:** user runs `meta list` (MAM listed); a client connect pulls
   the extra addon; smoke-test our plugin (kick, !menu, goal test); journal
   clean of `status=139`; `host_workshop_map` addon context intact after a
   `css_maprr`.
5. **Rollback:** remove the .vdf + restart.
6. Only after 1-5 pass: design the first real content addon (kick/bounce
   sounds first - smallest, highest feel value). Separate plan.

## Order
1. Task 1a+1b in one build (roundwin shipped OFF), deployed at the next
   window the user clears; in-game verify punish, then flip roundwin on for
   its test.
2. Task 2 pre-check anytime (read-only); staging + restart only at an
   agreed empty-server window.
