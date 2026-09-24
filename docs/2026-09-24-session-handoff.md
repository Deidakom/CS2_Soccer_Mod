# Session handover – 2026-09-24 (for a Claude Code session on the server)

You are running on the CS2 SoccerMod VPS itself. The previous session ran in a
cloud container without SSH access, so the owner relayed every command and log
by hand. Start by checking the live state (section 3); several things below
were last reported by the owner and may have changed.

## 1. Working with the owner

- The owner is not a developer. Give one copy-paste command at a time and say
  what a good result looks like. They write English and German.
- They connect from Windows PowerShell (`ssh root@212.87.212.58`).
- The root password was shared in an earlier chat. Remind them to change it
  (`passwd`) and to consider SSH keys.
- Never print `/etc/cs2-soccermod-test.env` (RCON password, GSLT) or put
  secrets into commands, commits or logs.
- `--force` on `update-server.sh` restarts the server even with players on;
  say so before using it.

## 2. Layout

| What | Where |
|---|---|
| Repository checkout | `/root/cs2-soccermod-src`, branch `claude/kind-allen-p88hzt` (tracks origin; not merged into `main` yet) |
| CS2 server | `/home/gameserver/cs2`, user `gameserver`, port 27017 |
| Service | `cs2-soccermod-test.service`, plus the drop-in `/etc/systemd/system/cs2-soccermod-test.service.d/map-addon.conf` (content never reviewed; read it) |
| Map | Workshop `3361075564`, `soccer_cssl_stadium_v8`, loaded with `+host_workshop_map` |
| Backups | `/home/gameserver/cs2-soccermod-backups/server-update-*/` (each has `rollback.sh`) |
| SteamCMD | `/home/gameserver/steamcmd/steamcmd.sh`; run it as `gameserver`, which has cached credentials for the Steam account `s5DgtGcb` |
| MultiAddonManager config | `/home/gameserver/cs2/game/csgo/cfg/multiaddonmanager/multiaddonmanager.cfg` |
| MultiAddonManager switch | `addons/metamod/multiaddonmanager.vdf` (armed); currently parked at `/root/multiaddonmanager.vdf.off` |

## 3. Check the live state first

```sh
cd /root/cs2-soccermod-src && git pull
sudo bash deploy/testserver/update-server.sh --check
sudo bash deploy/testserver/set-addons.sh --show
ls -l /home/gameserver/cs2/game/csgo/addons/metamod/multiaddonmanager.vdf /root/multiaddonmanager.vdf.off
journalctl -u cs2-soccermod-test.service --since "10 min ago" --no-pager | grep -cE "reloading map"
```

A healthy server shows the stack below, the map in A2S, and 0–2 map reloads.

## 4. State at handover (last reported)

**Stack, installed by `update-server.sh`.** CS2 1.41.8.2 (the 2026-09-22
update), Metamod 2.0 build 1469 (KHook), CounterStrikeSharp v1.0.375 and the
KHook build of the native bridge. MultiAddonManager v1.6.1 is installed but
**disarmed**: the owner moved its `.vdf` to `/root/` to stop a reconnect loop.
Players can join. The menu falls back to plain text and players use stock
skins.

**Not yet on the server** (pushed, waiting for
`update-server.sh --build-plugin`):
- `3d3507c`: the `!tp` camera prop is transparent. Other players saw a floating
  "ERROR" model behind anyone in third person.
- `a5f10ff`: kit models are only applied when a mounted addon really contains
  them. Before this, `css_sm2teammodel kits` without the jersey addon gave that
  player a black screen with the death/replay HUD.

**MultiAddonManager config.** The owner wrote it by hand:
`mm_extra_addons "3807366566,3797479770"`, `mm_extra_addons_timeout 60`,
`mm_addon_connection_timeout 120`, `mm_cache_clients_with_addons 1`,
`mm_cache_clients_duration 0`, `mm_block_disconnect_messages 1`,
`mm_addon_debug 1`. They may have changed the list to `3797479770` since.
`set-addons.sh --show` tells.

**Workshop items**

| ID | Content | Status |
|---|---|---|
| 3361075564 | CSF Football Stadium (map) | public, fine |
| 3797479770 | jersey kits "Soccermod Skins", original item | public; the server has it installed; use this one |
| 3807366566 | menu UI "SoccerMod Classic UI", re-published copy | **awaiting Steam approval**: the server's download gets "Access is denied" |
| 3807367334 | jersey copy "SoccerMod Football Kits" | awaiting approval; redundant, do not use |
| 3796041025 | original menu UI (uploaded by "Lev") | not accessible to anyone else; do not use |

## 5. Open tasks, in order

1. **Deploy the branch.** Run
   `sudo bash deploy/testserver/update-server.sh --build-plugin` (add `--force`
   only if the owner agrees to kick players). It must end with `Verified: ...`.
   Then have someone use `!tp`: no ERROR model.
2. **Bring the jerseys back.**
   1. Set the list to jerseys only:
      `sudo bash deploy/testserver/set-addons.sh 3797479770 --no-restart`.
   2. Re-arm MultiAddonManager:
      ```sh
      mv /root/multiaddonmanager.vdf.off /home/gameserver/cs2/game/csgo/addons/metamod/multiaddonmanager.vdf
      chown gameserver:gameserver /home/gameserver/cs2/game/csgo/addons/metamod/multiaddonmanager.vdf
      ```
   3. Restart: `systemctl restart cs2-soccermod-test.service`.
   4. Check for no `download failed`, 0–2 `reloading map` lines, and
      `[SM2DIAG] kit_models_missing` absent.
   5. Run `css_sm2teammodel kits` through RCON and have a player join. The
      first join reconnects once or twice while the jersey addon downloads.
3. **Add the menu once Steam approves it.** Test with
   `runuser -u gameserver -- /home/gameserver/steamcmd/steamcmd.sh +login anonymous +workshop_download_item 730 3807366566 +quit`.
   Only after `Success. Downloaded item`, run
   `sudo bash deploy/testserver/set-addons.sh 3807366566 3797479770`.
4. **Open the PR** from `claude/kind-allen-p88hzt` into `main`; the owner
   agreed. There is no PR template. List the ten commits of section 7.
5. **Verify `set-addons.sh`'s restart path on the real server.** Its
   pre-checks and config edit were simulated. The part after the restart
   (waiting for the map, reading MultiAddonManager's download results,
   removing a failing addon) was never run end to end.
6. **Read `map-addon.conf`** and document what it adds.
7. **Later.** Once `mm_addon_debug 1` is no longer needed, turn it off. Next
   feature candidate: the clickable menu from
   `docs/awesome-cs2-evaluation-2026-09-24.md` (cs2-ui-kit + `CustomHudLayout`).

## 6. Lessons from today (do not repeat)

- **Failed addon downloads loop.** MultiAddonManager reloads the map after
  every failed addon download. One undownloadable ID in `mm_extra_addons`
  (unapproved, private, typo) reloads the map about once a second and throws
  everyone out. Change the list only with `set-addons.sh` or
  `serverctl.py mam-addons`, which also reject placeholders.
- **`sed` matches too much.** `sed 's/^mm_extra_addons.*/…/'` also rewrites
  `mm_extra_addons_timeout`.
- **The listing check cannot see approval.** `serverctl.py workshop-status`
  only reads Steam's public listing, and an item awaiting approval looks "ok"
  there. Only an anonymous SteamCMD download proves players and the server can
  fetch it.
- **Keep the hook lines matched.** Metamod 1461+ loads only KHook plugins.
  Never install SourceHook builds: CounterStrikeSharp ≤ v1.0.374,
  MultiAddonManager ≤ v1.5.4, the old native bridge. `update-server.sh`
  enforces this; see `docs/cs2-1.41.8.2-server-stack-2026-09-24.md`.
- **"Required map is missing on your client"** came from MultiAddonManager
  (stale offsets in v1.5.4, then an undownloadable addon), not from the map.
- **The Steam "Game Info" window** says "app id specified by server is
  invalid" when the server does not answer at all (restarting or looping).
  Joining from the CS2 console (`connect 212.87.212.58:27017`) is more
  reliable.

## 7. Branch history (`claude/kind-allen-p88hzt`, oldest first)

| Commit | Change |
|---|---|
| `661a614` | ball lag compensation, body duels, menu page memory, escaping |
| `205153e` | `deploy/testserver/update-server.sh`, `serverctl.py` |
| `834c656` | hook-line guards (superseded default) |
| `1050c9b` | CounterStrikeSharp v1.0.375 + Metamod 1469 KHook stack, native bridge rebuild (`build-linux.sh`), update-script fixes |
| `ab691fe` | `docs/awesome-cs2-evaluation-2026-09-24.md` |
| `8da3226` | MultiAddonManager v1.6.1 via `update-server.sh` |
| `6c4917a` | `serverctl.py workshop-status`, `republish-addon.sh` |
| `402ae85` | `set-addons.sh`, `serverctl.py mam-addons` |
| `3d3507c` | invisible `!tp` camera prop |
| `a5f10ff` | kit models only when a mounted addon has them (`VpkDirectory.cs`) |

Tests: `npm test`, `python3 -m unittest discover -s test -p 'test_*.py'` and
`dotnet run --project test/managed/ActivityTests.csproj -c Release`. The
plugin DLL built against CounterStrikeSharp.API 1.0.373 (NuGet) binds cleanly
to v1.0.375; this was checked by comparing member references.

## 8. Useful commands

```sh
# Server answers? Which map?
python3 deploy/testserver/serverctl.py a2s 127.0.0.1 27017
# Console commands over RCON (password read from the env file, never printed)
python3 deploy/testserver/serverctl.py rcon 127.0.0.1 27017 /etc/cs2-soccermod-test.env "meta list" "css_plugins list"
# MultiAddonManager activity
journalctl -u cs2-soccermod-test.service --since "10 min ago" --no-pager | grep -iE "MultiAddonManager|download failed|reloading map"
# Plugin diagnostics
journalctl -u cs2-soccermod-test.service --since "10 min ago" --no-pager | grep -E "SM2DIAG|SM2NATIVE" | tail -n 50
```
