# CS2 SoccerMod v1.0 Beta — server package

This archive contains the ready-to-run CounterStrikeSharp plugin and every
compiled SoccerMod resource used by the tested server build. It does not bundle
CS2, Metamod, CounterStrikeSharp, or the Workshop stadium.

## Requirements

- A Counter-Strike 2 dedicated server.
- Metamod:Source 2.x.
- CounterStrikeSharp 1.0.373 or newer with .NET 10 plugin support.
- Workshop map `soccer_cssl_stadium_v8`, item `3361075564`.

The release was tested on Linux. The plugin DLL and Source 2 resources are
platform-independent and can also be copied to a Windows dedicated server.

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

4. Start or restart the server with Workshop item `3361075564`.
5. Run `css_plugins list` in the server console. It must contain:

   ```text
   "CS2 SoccerMod" (1.0 Beta)
   ```

The installer verifies the release checksums, refuses to continue when
CounterStrikeSharp is missing, and backs up every overwritten SoccerMod file
under `game/csgo/addons/soccermod-backups/<timestamp>/`.

## Windows/manual installation

Stop the server, then copy the contents of this archive's `game/csgo` folder
into the server's own `game/csgo` folder while preserving the directory
structure. Restart the server afterward.

## First administrator

Fresh installations intentionally contain no hard-coded administrator. From
the server console or RCON, grant your own SteamID64 the root flag:

```text
css_admin_add 7656119XXXXXXXXXX root
```

Existing installations keep their `soccermod_admins.json` during upgrades.
Never add that JSON file to a public release because it contains server-specific
permissions.

## Team appearance

The beta uses a red tint and Phoenix model for T, and a blue tint and SAS model
for CT. Administrators can adjust or disable the layers independently:

```text
css_sm2teamcolor on
css_sm2teamcolor t 255 40 40
css_sm2teamcolor ct 40 80 255
css_sm2teammodel on
```

The choices persist in `soccermod_match_settings.json`. Turning the model layer
off takes full effect as players respawn; turning the tint off restores normal
render color immediately.

## Stadium and configuration

Start the server with Workshop item `3361075564`, for example:

```text
+game_type 0 +game_mode 0 +map de_dust2 +host_workshop_map 3361075564
```

Recommended gameplay cvars are in `examples/soccermod_server.cfg`. Review and
copy that file into your own cfg setup; the installer does not overwrite server
configuration or passwords.

The public KICKOFF website integration is optional and targets the community's
own server. Other operators can use the server-only `css_sm2webcap_*` bridge
commands from their own authenticated coordinator, or simply run SoccerMod
without a website cap system.

## Updating

Stop the server and run `sudo bash install.sh` from the newer archive. Runtime JSON files
inside the plugin directory are preserved. The installer replaces only the DLL
and packaged Source 2 resources.

Project source and issue tracker:
https://github.com/Deidakom/CS2_Soccer_Mod
