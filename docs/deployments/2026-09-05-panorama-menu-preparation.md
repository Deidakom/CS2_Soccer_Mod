# Panorama menu preparation — 1.4.10-dev

Status: prepared on Windows, **not deployed or visually accepted**. The user
released the PC for CS2/Workshop Tools checks. Workshop Manager's new submission
form was submitted as `SoccerMod UI`, with the existing SoccerMod overview image
and exactly three compiled resources (14.67 KB), after explicit confirmation.
Workshop ID: [3796041025](https://steamcommunity.com/sharedfiles/filedetails/?id=3796041025).
Public visibility and Custom tag were selected. Workshop Manager reported success.
Steam shows automatic content review and moderator approval pending. An anonymous
server-side SteamCMD download returned `Access Denied`; rollout and client
acceptance are therefore paused without changing the running server.

Update: after approval, server download and MultiAddonManager v1.5.4 mounting
succeeded. Plugin 1.4.10-dev was installed with classic mode, but the mounted
`maps/scripts/...vjs` did not execute and readiness correctly retained the plain
fallback. Both dynamically created entities existed. The next candidate is
1.4.11-dev, which packages the exact same compiled script in the conventional
retail `scripts/vscripts` namespace. The obsolete generated path was removed;
the source stays in its repository folder and the build maps it explicitly.

## Implemented

- A 620px Panorama panel with seven readable choices, wrapping labels, high
  contrast, a persistent footer and disabled navigation shown in its fixed place.
- At most seven root destinations: Admin (gated), Play (gated), Positions,
  Ranking, Statistics, Settings, Help & Credits. Play retains Match, Reload Map
  and CAP with the original permission and website-CAP checks. Public mode
  still exposes only Help, Settings and Credits to non-admins.
- Every renderer uses 8 Back/Previous, 9 Next, 0 Close. Empty navigation keys
  cannot select content or leak into weapon selection while a menu is open.
- Classic shows seven choices per page; the constrained HTML fallback keeps
  three, plain keeps two plus heading/footer. The fallback is not a full panel.
- Disabled information rows hide their keys in Panorama, as in HTML/plain.
- Input refreshes the inactivity deadline. Disconnect clears the replicated
  menu for that slot so a later player cannot inherit it.
- Script readiness requires a probe that resolves the layout entity. This
  proves the script path, **not** that the client downloaded/rendered the panel.

Sprint gameplay and preferences are unchanged. The compact 1.4.9 HTML meter was
observed in-game and remains the fallback. A separate lower Panorama meter now
uses per-player state, white percentage, cyan active/red refill wings, and sends
only quantized changes. Disconnect/menu/mode changes clear its cached state.
Panorama bars do not activate the HTML flicker-suppression game-rule flag.
Previously ready HUD entities are recreated if removed by round cleanup; failed
initial readiness does not cause an every-tick retry loop. Actual client visual
acceptance of this new meter and lifecycle remains pending.

## Verified

- Windows checkout fast-forwarded from ae808ab to 936081d before editing;
  existing untracked `exceptions.txt` left alone.
- Valve resourcecompiler: all three resources compile, zero failures.
- `tools/build-classic-menu-addon.ps1 -UpdateReleasePayload` copies/checksums
  the freshly compiled resources into the committed release payload.
- Node: 109 tests pass, including execution of the real HUD script with a
  mocked per-player API. Existing bind tests now accept Windows CRLF.
- Managed suite passes, including 132 combinations of render mode, readiness,
  parent navigation and 0–46 options. Checks assert no lost/duplicate choices,
  fixed keys, capacities, parent/previous semantics and escaped HTML.
- All 15 Python tests pass on Linux in `/tmp/soccermod-ui-tests-U6Xgx5`, with
  fake systemd/temporary plugin data. The Bash deployment tests do not run
  directly under the bundled Windows Python environment.
- A local NuGet vulnerability lookup produced NU1900; build/tests succeeded
  with the existing pinned dependency. CI must perform the network check.
- Final plugin Release build succeeds with zero warnings/errors.
- Candidate DLL SHA-256:
  `0b2e87a8f687d3a497e1fe4789c8a3206c75f68600a51624ec1073b6c74c7fab`.
- Staged manager matches the staged archive's binary exactly:
  `5dde52b44fad26cc6ac18d49e46e63374df42d545256bc38ef0ec709a3bd01a2`.

## Observed server state (before any deployment)

Host `212.87.212.58:27017`, service `cs2-soccermod-test.service`, map
`soccer_cssl_stadium_v8`, Workshop `3361075564`. Only Natsu was connected.
Menu mode was HTML. Metamod listed CounterStrikeSharp 1.0.373 and the SoccerMod
native physics bridge, not MultiAddonManager. The manager config still had
empty addon IDs and activation remained withheld at
`/root/staging/multiaddonmanager.vdf.armfile`.

Live baseline is 1.4.9-dev; its documented DLL SHA-256 is
`f4349d171cbfa5ef2d78dca538a7547b684e911a7c5692c69591226f0da00257`.
Live hash was rechecked and matches; server had zero humans at the latest check.
Recheck players again immediately before deployment.

A non-disruptive backup of the DLL, menu settings and manager activation/config
has been created. Combined rollback:
`bash /home/gameserver/cs2-soccermod-backups/panorama-f19dSzIc/rollback.sh`.
It restores only these targeted files, retaining player data. Files absent at
backup time are moved into the backup on rollback instead of deleted. Refresh
the baseline if another developer deploys changes before activation.

The [upstream latest release](https://github.com/Source2ZE/MultiAddonManager/releases/tag/v1.5.4)
was still v1.5.4 when checked. This does not prove compatibility with the
current server. The [upstream instructions](https://github.com/Source2ZE/MultiAddonManager)
say extra addons download/mount and then trigger a map reload. Allow for that
reload and verify the manager with a connected player, not only an empty host.

## Remaining activation and acceptance

1. PC permission and inspection of the existing sprint meter are complete.
   Workshop Tools is installed at
   `E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive` and its
   addon list includes `soccermod_classic_ui`. Workshop Manager is logged in as
   Natsu. Publication succeeded; Steam approval is pending.
2. Once approved, retry anonymous download of Workshop `3796041025` and inspect
   its actual VPK contents. Do not mount an inaccessible addon or ship another
   copy of loose files as activation. The first download attempt automatically
   updated the standalone SteamCMD utility, not the CS2 server installation.
3. Before changing server state, make a fresh backup of the current plugin
   and settings plus both manager paths below. Record whether each exists:
   `game/csgo/addons/metamod/multiaddonmanager.vdf` and
   `game/csgo/cfg/multiaddonmanager/multiaddonmanager.cfg`. If replacing manager
   binaries/gamedata or base HUD resources, back those up too.
4. The inspected `/root/soccer-parity-stage-20260905/install-ball-handling.sh`
   accepts `DLL SHA256 preserve`, backs up the plugin, restarts, and emits a
   rollback script. Its rollback does **not** restore manager/config changes.
   Keep profile mode `preserve`; prepare a combined rollback covering those
   additional files before arming the manager.
5. Install/mount the UI beside the stadium, wait for genuine script readiness,
   then verify client download and rendering. Missing readiness must retain
   the fallback. Never forge the acknowledgement or write internal HUD vectors.
6. Check seven-item root, long labels/information rows, every submenu,
   46-control list, 8/9/0, spectator input, inactivity, round restart, reconnect
   and per-player isolation. Check real permission failures remain enforced.
7. Finish lower sprint placement only with visual verification; preserve its
   compact wide shape, white percent and active/refill colours.
8. After visual acceptance, merge to main, check CI, deploy/package with a
   fresh backup, record final hashes and the exact combined rollback command.

No live plugin, map, server name, manager activation or live configuration was
changed during this preparation.
