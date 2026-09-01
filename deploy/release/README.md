# CS2 SoccerMod v1.1.0 — server package

This archive contains everything the plugin itself needs to run: the
compiled CounterStrikeSharp plugin, the native Metamod physics bridge
(ball spin), the ball model, menu and stadium-radar resources. It does
**not** bundle CS2, Metamod, or CounterStrikeSharp themselves — install
those first.

## Requirements

- A Counter-Strike 2 dedicated server (Linux).
- Metamod:Source 2.x — <https://www.sourcemm.net/downloads.php?branch=stable>
- CounterStrikeSharp 1.0.373 or newer, with .NET 10 plugin support —
  <https://github.com/roflmuffin/CounterStrikeSharp/releases>
- Workshop map `soccer_cssl_stadium_v8`, item
  [`3361075564`](https://steamcommunity.com/sharedfiles/filedetails/?id=3361075564).

The native physics bridge (`soccermod_native.so`) is Linux-only. On
Windows, everything in this package works **except ball spin** — the
plugin runs fine without it.

## Linux installation

1. Stop the CS2 server.
2. Extract this archive outside the CS2 installation.
3. Run as root:

   ```bash
   bash verify.sh
   sudo bash install.sh
   ```

   The default CS2 root is `/home/gameserver/cs2`. Override it when needed:

   ```bash
   sudo env CS2_SERVER_ROOT=/srv/cs2 bash install.sh
   ```

4. Start (or restart) the server with the Workshop map, for example:

   ```text
   +game_type 0 +game_mode 0 +map de_dust2 +host_workshop_map 3361075564
   ```

5. In the server console, confirm both plugins loaded:

   ```text
   meta list
   ```
   must include `SoccerMod Native Physics Bridge`.
   ```text
   css_plugins list
   ```
   must include `"CS2 SoccerMod" (1.1.0)`.

The installer verifies the release checksums, refuses to continue if
CounterStrikeSharp or Metamod is missing, and backs up every overwritten
SoccerMod file under `game/csgo/addons/soccermod-backups/<timestamp>/`.

## Windows / manual installation

Stop the server, then copy the contents of this archive's `game/csgo`
folder into the server's own `game/csgo` folder, preserving the directory
structure — except `addons/soccermod_native/` and
`addons/metamod/soccermod_native.vdf`, which are Linux-only and should be
skipped. Restart the server afterward.

## First administrator

Fresh installations intentionally contain no hard-coded administrator.
From the server console or RCON, grant your own SteamID64 the root flag:

```text
css_admin_add 7656119XXXXXXXXXX root
```

Existing installations keep their `soccermod_admins.json` and every other
`soccermod_*.json` file during upgrades — the installer only replaces the
compiled plugin/resource files listed in `SHA256SUMS`.

## Recommended server config

`examples/soccermod_server.cfg` has the gameplay cvars this mod is tested
with (knife-only loadout, no warmup, no round limit, solid teammates,
respawn-on-death). Review it, copy or `exec` it from your own server
startup config — the installer never overwrites your existing cfg files.

## Cap system: in-game menu or your own website

Players can organize 6v6 caps two ways:

- **In-game (works out of the box):** `!cap` or `!menu → Cap` opens the
  built-in captain-pick menu. Nothing else to set up.
- **Your own website (optional):** the `kickoff/` folder in the source
  repository is a self-hostable Steam-login cap website that drives the
  same match via RCON. See
  <https://github.com/Deidakom/CS2_Soccer_Mod/tree/main/kickoff> if you
  want to run one for your own community.

## Team appearance

v1.1.0 tints T red / Phoenix model and CT blue / SAS model by default.
Administrators can adjust or disable each layer independently:

```text
css_sm2teamcolor on
css_sm2teammodel on
```

Toggles and RGB defaults persist in `soccermod_match_settings.json`.
Turning the model layer off takes effect as players respawn; turning the
tint off restores normal render color immediately.

## Updating

Stop the server and run `sudo bash install.sh` from the newer archive.
Every `soccermod_*.json` file the plugin writes at runtime is left
untouched; the installer only replaces the DLL, the native `.so`/`.vdf`,
and the packaged Source 2 resources.

Project source and issue tracker:
https://github.com/Deidakom/CS2_Soccer_Mod
