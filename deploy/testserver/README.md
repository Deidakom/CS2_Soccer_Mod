# CS2 SoccerMod MVP test server

This deployment is intentionally separate from the existing CS:S server:

- CS2 root: `/home/gameserver/cs2`
- systemd service: `cs2-soccermod-test.service`
- game port: `27017` (keeps the existing CSS main/test ports separate)
- client port: `27007`
- TV port: `27022`
- baseline Workshop map: CSF Football Stadium, item `3361075564`
- internal map name: `soccer_cssl_stadium_v8`
- lab addon retained on disk: `soccermod_phase1`

The first online baseline uses the unmodified CSF Workshop map. It starts on
`de_dust2` so Steamworks can initialize, then `host_workshop_map` downloads and
mounts item `3361075564` and changes to `soccer_cssl_stadium_v8`. The Phase 1
lab addon remains available for isolated ball-script work, but it is not
injected into the third-party Workshop map.

The stock August 2026 CS2 dedicated server remains fixed at 64 Hz with
sub-tick input. Supplying `-tickrate 128` does not change the measured server
loop interval. Do not add that flag unless Valve restores support.

## Server install

Install CS2 app `730` with SteamCMD into `/home/gameserver/cs2`. Do not reuse or
overwrite `/home/gameserver/css`.

Run `preflight.sh` first. After extracting the generated package, run
`install-or-update.sh` as root. Put a fresh app-730 GSLT and a new random RCON
password in `/etc/cs2-soccermod-test.env`, keep that file mode `0600`, then:

```text
systemctl enable --now cs2-soccermod-test.service
journalctl -u cs2-soccermod-test.service -n 200 --no-pager
```

The first proof is direct-IP connection to port `27017`, an A2S response with
`map=soccer_cssl_stadium_v8`, and a two-player check of the CSF map's built-in
left-click ball interaction and goal behavior. This establishes the stable
server/map baseline. Our own match commands, CAP flow, and map-independent ball
controller are the next implementation layer.

Clients should subscribe to CSF Football Stadium Workshop item `3361075564`.
The server also requests that item through Steam Workshop during startup.

## Updating after a CS2 update

Every CS2 update restores `game/csgo/gameinfo.gi`, which removes Metamod's
search path: Metamod, CounterStrikeSharp and SoccerMod then silently stop
loading while CS2 itself keeps running. CS2 updates can also require newer
Metamod and CounterStrikeSharp builds. `update-server.sh` handles all of it
from a checkout of this repository on the server:

```sh
git clone https://github.com/Deidakom/CS2_Soccer_Mod /root/cs2-soccermod-src
cd /root/cs2-soccermod-src
sudo bash deploy/testserver/update-server.sh --check
sudo bash deploy/testserver/update-server.sh --build-plugin --sync-payload
```

`--check` changes nothing. It reports the installed and latest CS2 build,
the gameinfo.gi state, the installed and latest Metamod/CounterStrikeSharp
versions, players and loaded plugins.

An update run first downloads the pinned Metamod 2.0 drop and
CounterStrikeSharp release (with runtime) and builds the plugin. It waits for
an empty server (`--force` skips that, `--wait-minutes` bounds it) and backs
up the mod stack. Then it updates CS2 with SteamCMD, re-adds the Metamod
search path and installs only what changed. Finally it verifies map, Metamod,
CounterStrikeSharp, the native bridge and SoccerMod over A2S/RCON. A new
plugin DLL that does not load is rolled back automatically.

Every run prints its backup directory. Its `rollback.sh` restores the
previous Metamod, CounterStrikeSharp runtime, native bridge, DLL and payload
files. Ranks, admins, bans and settings keep their latest values. CS2 itself
cannot be downgraded.

Metamod, CounterStrikeSharp and the native bridge must use the same hook
line. CS2 1.41.8.2 (2026-09-22) needs CounterStrikeSharp v1.0.375, the first
KHook release, on Metamod build 1467 or newer. The script therefore installs
Metamod build 1469 + CounterStrikeSharp v1.0.375 by default, together with
the native bridge built for KHook, and refuses mismatched pairs before
changing anything. An installed MultiAddonManager moves to v1.6.1, the KHook build with the
1.41.8.2 offsets; its addon list and armed state stay as they are. Its old
build sends joining players a broken addon list ("Required map is missing on
your client"). Details:
[docs/cs2-1.41.8.2-server-stack-2026-09-24.md](../../docs/cs2-1.41.8.2-server-stack-2026-09-24.md).

After a future CS2 update, first check whether CounterStrikeSharp published a
matching release (`--check` shows the latest one). Pass it with
`--cssharp-version` (and `--metamod-build` if its docs ask for a newer
Metamod). The native bridge takes its `AcceptInput` signature from
CounterStrikeSharp's gamedata, so it follows without a rebuild.

## Workshop addons players must download

MultiAddonManager makes every joining player download each item in
`mm_extra_addons` (menu UI, jersey kits). An item that is private,
friends-only, unapproved or broken is invisible to everyone but its owner, and
joining then fails with "Required map is missing on your client". `--check`
lists every item and whether other players can download it
(`serverctl.py workshop-status ID...` does the same on its own).

`republish-addon.sh` re-publishes an addon from the server's Workshop cache as
a new public item under your own Steam account:

```sh
sudo bash deploy/testserver/republish-addon.sh login STEAM_LOGIN
sudo bash deploy/testserver/republish-addon.sh publish STEAM_LOGIN OLD_ID "Title"
```

Run it in your own terminal: SteamCMD asks for the password and Steam Guard
code itself. It then prints the new id and the remaining steps.

Change the addon list only with `set-addons.sh`. It test-downloads each addon
anonymously the way the server does and refuses any that Steam will not hand
out. That includes new items still awaiting approval, which MultiAddonManager
would otherwise retry, reloading the map about once a second and throwing
every player out. It then edits only the `mm_extra_addons` line, restarts the
server and takes out any addon the server itself still fails to download:

```sh
sudo bash deploy/testserver/set-addons.sh --show
sudo bash deploy/testserver/set-addons.sh 3807366566 3797479770
sudo bash deploy/testserver/set-addons.sh none
```
