# CS2 SoccerMod

**CS2 SoccerMod v1.0 Beta** is a CounterStrikeSharp port of the classic
SoMoE-19 Soccer Mod for Counter-Strike 2. It combines a native VPhysics ball,
knife-based football controls, match administration, statistics, referee tools,
and number-key menus on the `soccer_cssl_stadium_v8` Workshop map.

The public beta is running on the project's test server and is packaged for
fresh Linux or Windows CS2 dedicated-server installations.

## Download a server-ready build

Download the latest archive from
[GitHub Releases](https://github.com/Deidakom/CS2_Soccer_Mod/releases).
The release contains the compiled plugin DLL, ball model, classic-menu assets,
stadium radar/loading resources, checksums, a Linux installer, Windows copy
instructions, and a recommended server cfg.

Requirements:

- Counter-Strike 2 dedicated server
- Metamod:Source 2.x
- CounterStrikeSharp 1.0.373 or newer with .NET 10 plugin support
- Workshop item [`3361075564`](https://steamcommunity.com/sharedfiles/filedetails/?id=3361075564)
  (`soccer_cssl_stadium_v8`)

Linux quick start after Metamod and CounterStrikeSharp are installed:

```bash
unzip CS2-SoccerMod-1.0-beta.1-server.zip
cd CS2-SoccerMod-1.0-beta.1-server
bash verify.sh
sudo bash install.sh
```

Restart the server, run `css_plugins list`, and confirm it reports:

```text
"CS2 SoccerMod" (1.0 Beta)
```

Fresh installations deliberately contain no hard-coded administrator. Grant
your own SteamID64 once from server console or RCON:

```text
css_admin_add 7656119XXXXXXXXXX root
```

The archive README contains the complete installation, update, backup, Windows,
map, and configuration instructions.

## Included gameplay

- Native symmetric VPhysics football and primary-knife kick input
- Standing/crouched power, airborne normalization, wall-bounce tuning, and
  ball/player impact
- Goal detection, round resets, match periods, pauses, ready checks, forfeits,
  referee cards, and score control
- Sprint, goalkeeper areas, AFK handling, health normalization, statistics,
  rankings, and server administration
- Red-versus-blue team tinting with uniform Phoenix/SAS stock player models;
  both layers have independent persistent admin toggles
- HTML/classic number-key menus plus stadium radar and loading-screen resources
- Optional KICKOFF website cap bridge with team assignment, automatic spawn,
  temporary position tags, and explicit cap-end cleanup that restores normal
  team selection

The public KICKOFF site targets the community server. Other operators can run
the plugin without it or connect the authenticated server-only
`css_sm2webcap_*` commands to their own coordinator.
Coordinators should call the server-only `css_sm2webcap_clear` command whenever
a cap is dismissed or closed so assignments cannot remain active after play.

## Build from source

The live plugin is `src/server-plugin/SoccerModMvp`. It targets .NET 10 and uses
CounterStrikeSharp.API 1.0.373.

```powershell
dotnet build src/server-plugin/SoccerModMvp/SoccerModMvp.csproj -c Release
npm test
```

The assembly remains named `SoccerModNativeHull.dll` for compatibility with
existing installations; its public plugin identity is `CS2 SoccerMod v1.0 Beta`.

`tools/build-public-release.ps1` assembles the installable archive from the
Release DLL and compiled project-owned Source 2 resources. Those generated
binaries are attached to GitHub Releases rather than committed to the source
tree.

## Repository map

- `src/server-plugin/SoccerModMvp/` — live CounterStrikeSharp plugin
- `src/assets/` — authored ball model and physics-hull sources
- `src/workshop-addon/` — classic menu and stadium radar source assets
- `deploy/release/` — public installer, verification, cfg, and archive README
- `deploy/testserver/` — project-specific test-server deployment
- `test/` — automated regression suite
- `docs/` — implementation, calibration, audit, and session handoff notes

The original CS:S implementation used for behavioral reference is
[MK99MA/SoMoE-19](https://github.com/MK99MA/SoMoE-19). This repository is a
clean-room CS2 port and does not compile the SourceMod code directly.

## Beta status

This is a prerelease. Back up an existing server and validate the plugin on a
staging instance before production use. Report reproducible issues through the
[GitHub issue tracker](https://github.com/Deidakom/CS2_Soccer_Mod/issues).
