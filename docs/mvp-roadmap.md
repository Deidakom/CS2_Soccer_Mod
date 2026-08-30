# CS2 SoccerMod roadmap: playable MVP first

Date: 2026-08-27

## Product direction

The final objective remains the closest practical 1:1 recreation of the
existing CS:S SoccerMod. Development is intentionally split into two goals:

1. ship a playable CS2 MVP quickly enough for real player testing;
2. use those tests to iterate toward full CS:S behavior and feature parity.

Exact restitution bands, exhaustive physics repeats, long soak gates, and
secondary competitive features do not block the first playable build.

## MVP acceptance target

The first testable build must provide:

- one server-authoritative football that all players see;
- one CSS-like knife kick on primary attack (left click), with bounded speed;
- two playable teams and practical one-versus-one or multiplayer play;
- goal detection, team score, a short goal pause, and center reset/kickoff;
- a basic match lifecycle: warmup, countdown, live, pause, restart, and finish;
- configurable time or score limits;
- player-safe football rules, including suppression of ordinary player damage;
- a minimal cap flow using player chat:
  - open or cancel a cap;
  - join and leave the player pool;
  - select two captains;
  - alternate player picks and assign teams;
  - ready both sides and start the match;
- concise player-facing status/score feedback;
- safe handling of disconnects, team changes, round restart, and map reload.

The installed Valve `point_script` declaration directly exposes the required
MVP primitives: `OnPlayerChat`, `GetAllPlayerControllers`, `JoinTeam`, player
connect/disconnect/reset callbacks, round callbacks and timing, knife attacks,
custom HUD layouts, and the already-proven ball transform/trace functions.

## Implementation order

### Slice 1: playable match loop

Build a small pure match-state core around the existing proven ball, kick,
goal, and strict reset code. Add score, kickoff, countdown, pause, time/score
limits, player-damage suppression, server commands, telemetry, and tests.

### Slice 2: basic cap flow

Add a pure cap-state core and chat-command adapter. Start with deterministic
captain/pick behavior and team assignment; richer menus and persistence can
follow after multiplayer testing.

### Slice 3: practical multiplayer test

Run at least one real two-player match. Tune the single left-click kick, cooldown, ball
speed, and goal reset from player feedback. Fix only reproducible gameplay
blockers before packaging the first test release.

### Slice 4: test release

Embed the final runtime in a retained Hammer build, verify a clean load, and
publish or distribute the owned addon/server configuration needed by testers.

## Deferred parity work

These remain required for the long-term 1:1 objective but do not block MVP:

- exhaustive drop, roll, wall-angle, restitution, and spin calibration;
- a formal primary-input endurance gate;
- exact CS:S command names, menus, sounds, HUD styling, and configuration;
- complete captain, substitution, goalkeeper, admin, statistics, and match
  administration behavior from the original mod;
- persistence, database integration, match reporting, and demo workflows;
- large-player-count replication and long unattended soak tests;
- final ball-feel comparison against matched CS:S captures;
- CSF Football Stadium integration.

The subscribed CSF Football Stadium is still a compiled reference package,
not editable source. The MVP can be proven on the owned writable lab map.
Literal integration with the Stadium still requires either editable permitted
source or a separately qualified server-plugin route.

## Current evidence threshold

The MVP decision is supported by live proof of authoritative ball binding,
knife-driven primary-kick writes, real motion, goals, strict reset, lifecycle
reloads, and automated physics runs. The dedicated fixture matrix currently
has passing three-trial results for all drop profiles, all X/Y roll profiles,
and the straight-wall profile. The two angled-wall profiles are useful later
calibration evidence and are no longer an MVP blocker.

## Current implementation checkpoint

The first two software slices are implemented and pass 70/70 automated tests:

- warmup, countdown, live, goal pause, pause/resume, finish, score and clock;
- live-goal scoring wired to the proven goal/reset path;
- player-damage suppression;
- chat match commands and development HUD status;
- cap open/join/leave/cancel, deterministic captains, alternating picks,
  automatic T/CT assignment, automatic match start, and disconnect safety.

The exact tested bundle is staged in the owned `soccermod_phase1` addon. The
next gate is the short one-player and two-player procedure in
`docs/mvp-manual-test.md`, followed by a retained Hammer package build.
