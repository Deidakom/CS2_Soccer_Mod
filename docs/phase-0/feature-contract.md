# Feature contract and delivery tiers

The purpose of this contract is to prevent “similar to CSS” from becoming an
unbounded or unverifiable target. A feature is complete only when its acceptance
test passes on the dedicated-server test matrix.

## Tier 0 — Phase 1 feasibility gate

| Capability | Required behavior | Proposed owner |
|---|---|---|
| Authoritative ball | One server-owned spherical ball with stable rolling, bounce, collision, sleep/wake, and reset behavior. | Map entity + `cs_script` |
| Explicit kick | Primary knife input (left click) produces the single validated CSS-style kick; distance, cone, cooldown, obstruction, and max speed are enforced. Secondary attack and lob are not SoccerMod controls. | `cs_script` |
| Touch attribution | Accepted kicks record player, team, time, position, and kick type. | `cs_script` |
| Goal detection | High-speed crossing of either goal plane produces exactly one event; players and stray props never score. | Trigger + scripted plane-crossing fallback |
| Reset | Goal/admin/reset returns the ball to midfield with zero linear/angular velocity and no stale score event. | `cs_script` |
| Network test | Ball remains authoritative and usable at representative latency/loss with two and twelve players. | Dedicated-server test |
| Clean content delivery | A clean client receives the map, ball, collision, materials, sounds, and scripts from the private Workshop addon. | Workshop addon |

No other feature may conceal a Tier 0 failure.

## Tier 1 — playable MVP

| Capability | CSS behavior to preserve | Proposed owner |
|---|---|---|
| Teams and capacity | T/CT soccer teams, spectator join, valid spawns, reconnect/hot join. | Map first; adapter only for gaps |
| Soccer match state | Warmup, ready, kickoff, live, paused, halftime, second period, golden goal, finished. | `cs_script` state machine |
| Match defaults | Two 15-minute periods, short break, golden goal; values configurable. | `cs_script` configuration |
| Kickoff | Ball centered, players reset, wrong-side entry prevented until play begins. | Map geometry + script |
| Score and clock | Independent soccer score/clock that does not depend on native CS round score. | `cs_script` + map HUD/fallback |
| Goal attribution | Scorer, assist, and own goal derived from an explicit documented touch-history policy. | `cs_script` |
| Safe players | Knife-only or weaponless soccer loadout, no lethal damage, reliable respawn/rejoin. | Map first; adapter if required |
| Minimal operations | Start, stop, pause, resume, reset ball/map, set team names, and inspect state. | Map UI; optional adapter commands |
| Match log | Structured local event stream sufficient to diagnose every goal and state transition. | Map bridge or adapter |

## Tier 2 — competitive release

- Captain/cap fight, player picking, preferred positions, server lock, and ready
  checks.
- Persistent SteamID identity and SQLite statistics.
- Goals, assists, own goals, hits, passes, interceptions, losses, saves, result,
  MVP, MOTM, and rankings with explicit definitions.
- Goalkeeper selection and goalkeeper-area enforcement.
- Referee tools, yellow/red cards, punishment, forfeit, and administrative score
  correction.
- Configurable sprint, only if the movement spike avoids unsafe memory hooks and
  produces stable prediction.
- CSTV/demo recording, crash-safe finalization, and retention metadata.
- Production permissions, public commands, map change/restart recovery, and
  update rollback.

Tier 2 is the likely boundary for a “competitive SoccerMod,” but it is not part
of the ball-lab commitment.

## Tier 3 — parity and polish backlog

- Advanced training, multiple balls, cannons, cones, targets, and goal-trigger
  toggles.
- Dynamic name/number jerseys and goalkeeper skins.
- Third-person camera.
- Shouts, sound menus, custom messages, richer HUD, celebrations, and visual
  presentation.
- AFK management, dead chat, join-order utilities, comprehensive in-game
  settings, and admin management UI.
- Map variants, grass/pitch replacement concepts, and optional integrations.
- Experimental 1.5.18-only cap/clantag behavior after a separate review.

## Explicitly excluded until separately approved

- Instant replay.
- Transparent compatibility with arbitrary legacy CSS soccer maps.
- Runtime tick-rate switching as a gameplay feature.
- Direct SourcePawn compatibility.
- A required native C++ physics hook.
- Exact CSS third-person implementation.
- Exact pixel/asset reproduction where ownership or redistribution rights are
  not confirmed.

## Cross-cutting completion definition

Every delivered capability must have:

1. A documented owner and state transition.
2. Server-side validation for any player input.
3. Structured diagnostic logging.
4. Reconnect, map-change, and restart behavior.
5. A real dedicated-server test; listen-server-only evidence is insufficient.
6. A clean-client content test for every referenced asset.
7. A rollback path and the exact CS2/API/framework versions recorded.
