# Changelog

All notable changes to CS2 SoccerMod are documented here. See
`docs/releases/` for the long-form notes behind each entry.

## [Unreleased]

- Fixed `!tpf` (front-facing third-person camera): it orbited on the pawn's
  body rotation, which does not reliably track facing in CS2, causing the
  reported flicking. It now orbits on the same view yaw `!tp` uses, looks at
  a chest-height target computed from the camera's actual smoothed position,
  and clamps against nearby walls. `!tp` while `!tpf` is active now switches
  to the rear camera instead of turning third person off (and vice versa).
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
