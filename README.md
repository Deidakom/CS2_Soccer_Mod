# CS2 SoccerMod

This directory is the clean-room planning and implementation area for a new
Counter-Strike 2 SoccerMod. The existing Counter-Strike: Source material in
`../ball-reference-analysis` is a behavioral reference, not code that can be
compiled for CS2.

## Current status

The Phase 1 precondition gate is green. The subscribed CSF Soccer Stadium has
been package-audited and live-loaded, the current Workshop Tools/API are
fingerprinted, and the pure plus runtime-bundle suite passes 70/70 tests.
Hammer's map build and revised incremental package build passed. An exact
runtime failure proved that CS2 rejects relative JavaScript imports, so staging
now produces one self-contained adapter
with only Valve's supported `cs_script/point_script` import. A fresh local load
then passed script activation, API-input, command, and atomic-reset smoke checks.
The rebuilt package passes the same live smoke. A physical-input diagnostic has
now also proved the official knife callback and accepted/rejected kick
calculation paths. The ball, kick, goal, reset, lifecycle, dedicated physics
fixtures, playable match state, damage suppression, and basic cap draft now
pass their automated gates. The next MVP gate is a retained Hammer build,
followed by one real two-player gameplay test and a clean delivery check.

Phase 1 is now closed at the MVP-feasible threshold. The immediate priority is
deploying the current build to a separate CS2 test server and iterating from
real multiplayer feedback.

The current recommendation is:

- Put the ball, kicks, goals, resets, match state, and fallback HUD in our
  project-owned, writable Workshop map using Valve's official `cs_script` API.
- Add a thin Metamod 2.x + CounterStrikeSharp adapter later for administration,
  normal public commands, persistence, and CSTV only when the official map layer
  cannot provide them safely.
- Do not approve the full build until the Phase 1 ball lab passes real remote
  client and clean-download tests.

## Test-server deployment

`tools/build-testserver-package.ps1` creates a hash-pinned Linux deployment
package from the compiled local addon. The service, config, preflight, and
install notes live in [deploy/testserver](deploy/testserver/README.md). This
first server build uses the official map script directly; Metamod and
CounterStrikeSharp remain optional follow-up components.

## Phase 0 documents

- [Phase 0 report](docs/phase-0/phase-0-report.md)
- [CSS baseline](docs/phase-0/css-baseline.md)
- [Feature contract](docs/phase-0/feature-contract.md)
- [Toolchain readiness](docs/phase-0/toolchain-readiness.md)
- [Risk register](docs/phase-0/risk-register.md)
- [Phase 1 ball-lab specification](docs/phase-0/phase-1-ball-lab.md)

## Phase 1 documents

- [Current Phase 1 status](docs/phase-1/phase-1-status.md)
- [Installed toolchain fingerprint](docs/phase-1/toolchain-fingerprint.md)
- [CSF Football Stadium audit](docs/phase-1/csf-football-stadium-audit.md)
- [Ball-lab test protocol](docs/phase-1/ball-lab-test-protocol.md)
- [Telemetry contract](docs/phase-1/telemetry-contract.md)

Run `tools/phase0-verify.ps1` from PowerShell to re-audit the local baseline and
detect source or toolchain drift.

Run `tools/phase1-verify.ps1` to verify the Stadium package hash, Workshop Tools
gate, current Valve scripting declaration, and pure core tests.

After Valve Addon Manager creates an addon named `soccermod_phase1`, run
`tools/stage-phase1-addon.ps1`. It stages the lab without creating or
overwriting Valve addon metadata, and generates the single-file CS2 runtime
bundle during staging.

## MVP player commands

- `!start`, `!restart`, `!pause`, `!resume`, `!stop`, `!score`
- `!cap` opens a cap and joins its creator; other players use `!join` or
  `!leave`.
- The cap owner uses `!draft`. The first two joined players become captains.
- Captains alternate `!pick <player-slot>` until both teams are complete.
- A completed draft assigns teams and starts the match automatically.
- `!teams` shows cap state, `!play` retries a ready cap, and `!cancelcap`
  clears it.
