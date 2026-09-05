# CS2 SoccerMod

[![Build](https://github.com/Deidakom/CS2_Soccer_Mod/actions/workflows/build.yml/badge.svg)](https://github.com/Deidakom/CS2_Soccer_Mod/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Deidakom/CS2_Soccer_Mod?include_prereleases)](https://github.com/Deidakom/CS2_Soccer_Mod/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A CounterStrikeSharp port of the classic SoMoE-19 Soccer Mod for
Counter-Strike 2 — knife-only football with a native physics ball, real
spin, match administration, a captain-pick cap system, a training menu,
statistics, and referee tools, all on the `soccer_cssl_stadium_v8`
Workshop map.

See the [visual project overview](docs/OVERVIEW.md) and the
[September 2026 code review](docs/REVIEW-2026-09-05.md).

## Download

Grab the latest server package from
**[GitHub Releases](https://github.com/Deidakom/CS2_Soccer_Mod/releases)** —
it contains everything the mod needs to run (plugin, native physics
bridge, ball model, menus, stadium resources) as a single ZIP with an
installer.

## Community

The mod's official community page is the Steam group
**[cs2soccermod](https://steamcommunity.com/groups/cs2soccermod)** —
join for announcements, server links, and to talk to other hosts and
players.

## Requirements

| Component | Tested version | Get it |
|---|---|---|
| CS2 dedicated server | Linux (Windows works without ball spin) | — |
| Metamod:Source | 2.x | <https://www.sourcemm.net/downloads.php?branch=stable> |
| CounterStrikeSharp | 1.0.373+ (.NET 10 plugins) | <https://github.com/roflmuffin/CounterStrikeSharp/releases> |
| Workshop map | `soccer_cssl_stadium_v8`, item [`3361075564`](https://steamcommunity.com/sharedfiles/filedetails/?id=3361075564) | Steam Workshop |

## Installation

1. Install a CS2 dedicated server, Metamod:Source, and CounterStrikeSharp
   (links above) — this mod does not include or replace any of them.
2. Start the server on Workshop item `3361075564`
   (`+host_workshop_map 3361075564`).
3. Download the latest release ZIP, extract it, and run:
   ```bash
   bash verify.sh
   sudo bash install.sh
   ```
4. Restart the server, then confirm in console: `meta list` shows
   **SoccerMod Native Physics Bridge**, `css_plugins list` shows
   **"CS2 SoccerMod" (1.1.0)**.
5. Grant yourself admin once, from server console or RCON:
   ```text
   css_admin_add <your SteamID64> root
   ```

The archive's own `README.md` has the full Linux/Windows/update
instructions. `examples/soccermod_server.cfg` has the recommended
gameplay cvars — review before adding it to your startup config.

## Playing

`!menu` is the front door for everything — Match, Cap, Training (admin),
Ranking, Statistics, Positions, Help, Settings. The full command list,
including chat shortcuts like `!sprint`, `!gk`, `!tp`, and `!kill`, is in
**[docs/COMMANDS.md](docs/COMMANDS.md)**.

### Cap system: menu or your own website

Players organize 6v6 games ("caps") two ways:

- **In-game (works immediately):** `!cap` opens the captain-pick menu —
  put players to spectator, add randoms, run a knife/weapon fight to pick
  captains, then alternating picks. Nothing to configure.
- **Your own website (optional):** the [`kickoff/`](kickoff/) folder is a
  self-hostable, Steam-login cap website that drives the same match over
  RCON, if you'd rather run one for your community. See
  [`kickoff/README.md`](kickoff/README.md).

### Admin

Five permission tiers, least to most access: `match` < `admin` <
`soccermod` (implies `admin`+`match`) < `ball` < `root`. A fresh install
has no admin — grant your own SteamID64 `root` as shown above; everything
else (including promoting other players to the `soccermod` tier) can be
done in-game from `!menu → Admin`. Full flag/command breakdown in
[docs/COMMANDS.md](docs/COMMANDS.md#admin--permissions).

## Included gameplay

- Native symmetric VPhysics football, knife-only kick input, and real
  spin on kicks and wall rebounds via the bundled native Metamod plugin.
- Goal detection calibrated by measuring the actual map geometry (posts,
  crossbar, goal line) rather than guessed constants — a shot that misses
  the frame is rejected and logged, not just "close enough."
- Match periods, pauses, ready checks, forfeits, referee cards, and score
  control; a captain-pick Cap system and a Training menu (cannon,
  personal cannon, ball spawn) both ported 1:1 from the original SoMoE
  menus.
- Sprint with a cooldown and optional chat messages, goalkeeper areas, AFK handling, health
  normalization, statistics, rankings, and full server administration.
- Red-versus-blue team tinting with uniform stock player models,
  independently toggleable.
- Plain-text, HTML, and classic number-key menus, plus stadium radar and
  loading-screen resources.
- Optional KICKOFF website cap bridge (see above) with team assignment,
  automatic spawn, and spectator handling for non-participants.

## Build from source

The plugin lives in `src/server-plugin/SoccerModMvp`, targets .NET 10,
and uses CounterStrikeSharp.API 1.0.373:

```bash
dotnet build src/server-plugin/SoccerModMvp/SoccerModMvp.csproj -c Release
npm test
```

The native Metamod plugin (`src/native-plugin/soccermod_native/`) is a
standard AMBuild project against `hl2sdk-cs2` and `metamod-source`; see
its own `README.md` for build instructions. Prebuilt for Linux and
attached to every release.

`tools/build-public-release.ps1` assembles the installable archive from
the built DLL and the committed `deploy/release/payload/` tree
(everything that isn't practical to rebuild from source in CI — compiled
Source 2 resources, the native `.so`). Tagging a commit `vX.Y.Z` and
pushing the tag runs the same build in GitHub Actions and publishes the
release automatically.

## Repository map

- `src/server-plugin/SoccerModMvp/` — the CounterStrikeSharp plugin
- `src/native-plugin/soccermod_native/` — native Metamod physics bridge (spin)
- `src/assets/` — authored ball model and physics-hull sources
- `src/workshop-addon/` — classic menu and stadium radar source assets
- `kickoff/` — optional self-hostable cap website (frontend + auth + RCON bridge)
- `deploy/release/` — public installer, verification, cfg, payload, and archive README
- `deploy/testserver/` — project-specific test-server deployment
- `docs/` — command reference, release notes, implementation and calibration notes
- `test/` — automated regression suite

## Credits

The original CS:S implementation used for behavioral reference is
[MK99MA/SoMoE-19](https://github.com/MK99MA/SoMoE-19) (originally by
Marco Boogers). This repository is a clean-room CS2 port — it does not
compile or redistribute any SourceMod code. Runs on the
[CSF Football Stadium](https://steamcommunity.com/sharedfiles/filedetails/?id=3361075564)
Workshop map, [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp),
and [Metamod:Source](https://www.sourcemm.net/).

## Status

This is a pre-release. Back up an existing server and validate on a
staging instance before production use. Report reproducible issues
through the [GitHub issue tracker](https://github.com/Deidakom/CS2_Soccer_Mod/issues)
or the [official Steam group](https://steamcommunity.com/groups/cs2soccermod).

## License

[MIT](LICENSE).
