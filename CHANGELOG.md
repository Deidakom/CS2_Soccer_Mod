# Changelog

All notable changes to CS2 SoccerMod are documented here. See
`docs/releases/` for the long-form notes behind each entry.

## [Unreleased]

- Fixed AFK snapshots retaining live entity memory; poll once per second
  and reuse the player list instead of scanning twice every tick.
- Preserve assists during repeated dribble touches, clear ball-touch credit
  on ball resets/disconnects, and clear per-match stats at the next start.
- Index stats by SteamID and save on map end and plugin unload. Restore
  temporary AFK passwords and goal-respawn settings on those exit paths.
- Fix KICKOFF database connection cleanup, malformed session handling,
  login redirect validation, fragmented/multi-packet RCON responses, and
  concurrent RCON batch imports.
- Add the missing KICKOFF environment template, persistent writable data
  volume, explicit bridge gateway, and separate service credentials.
- Refresh stale feature checks and add Python and managed regression tests.

- Warmup goals that aren't own goals no longer reset the ball to centre -
  it's left where it landed. Own goals are unchanged.
- Fixed jumping over the ball: the body-push and body-impact physics no
  longer act on a player whose feet have already cleared the ball's top
  surface, so a jump that clears the ball (a normal jump does) is no
  longer shoved back down onto it. Ball size is unchanged.
- Ball menu: new "Restore defaults" entry (confirm-gated) and
  `css_sm2ball_defaults` console command, resetting spin, air-kick,
  left/right-click power, body-push, kick sound, impact, settle and
  elevation to their defaults in one step.
- Ball collision group reverted to 0 (regular solid) - group 20
  (non-solid to players) didn't help the jump-over issue and is no
  longer needed now that the push/impact fix addresses it directly.
- Established the official community page: the Steam group
  [cs2soccermod](https://steamcommunity.com/groups/cs2soccermod),
  linked from the README and the in-game `!menu -> Help -> Project
  links`.

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
