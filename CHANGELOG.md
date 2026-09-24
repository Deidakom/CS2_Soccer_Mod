# Changelog

All notable changes to CS2 SoccerMod are documented here. See
`docs/releases/` for the long-form notes behind each entry.

## [Unreleased]

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
