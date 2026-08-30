# MultiAddonManager v1.5.4 — staged, not loaded (2026-08-30)

Downloaded from `Source2ZE/MultiAddonManager` release v1.5.4 (2026-08-18),
asset `MultiAddonManager-v1.5.4-steamrt3.tar.gz` (1.87 MB) — steamrt3 is
correct for this server's Ubuntu 24.04 (steamrt4 is for Ubuntu 25+).
Pre-check: gamedata predates the 2026-08-24 CS2 update, but zero breakage
issues filed against it since — proceeding as a rollback-ready experiment,
same caution class as the CS2Fixes spin signature.

## Current state — genuinely inert

Placed (harmless on their own, Metamod never reads them unless told to):
- `/home/gameserver/cs2/game/csgo/addons/multiaddonmanager/bin/multiaddonmanager.so`
- `/home/gameserver/cs2/game/csgo/cfg/multiaddonmanager/multiaddonmanager.cfg`
  (defaults; `mm_extra_addons` empty)

Withheld (this is the file that actually activates it):
- `/root/staging/multiaddonmanager.vdf.armfile`

Confirmed via `ls addons/metamod/` that no `multiaddonmanager.vdf` is
present there — Metamod will not discover or load the plugin on its next
restart in this state.

## To arm it (USER action — loading native code into the live server
process is the standing rule the agent is blocked from)

```bash
ssh cs2-soccermod 'cp /root/staging/multiaddonmanager.vdf.armfile /home/gameserver/cs2/game/csgo/addons/metamod/multiaddonmanager.vdf && chown gameserver:gameserver /home/gameserver/cs2/game/csgo/addons/metamod/multiaddonmanager.vdf'
```

Then either:
- restart the service at an empty-server window (`systemctl restart cs2-soccermod-test.service`), or
- run `meta load multiaddonmanager` from RCON/console on the live server (no restart, but same "loading native code" caution applies).

## Verify after arming
1. `meta list` — `multiaddonmanager` should appear, loaded.
2. journal clean of `status=139` for a few minutes with a player connected
   (this is exactly the failure mode from earlier today's CheckTransmit
   crash — watch for it).
3. Set a real test addon id in `cfg/multiaddonmanager/multiaddonmanager.cfg`
   (`mm_extra_addons "<id>"`), reconnect, confirm the client downloads it.
4. Smoke-test SoccerMod itself: kick, `!menu`, `css_sm2goal_test` — confirm
   nothing about our own plugin regressed.
5. `css_maprr` — confirm the workshop map addon context still survives a
   reload with MAM active.

## Rollback
```bash
ssh cs2-soccermod 'rm /home/gameserver/cs2/game/csgo/addons/metamod/multiaddonmanager.vdf'
```
then restart. Leaves the inert binary/cfg in place (harmless); delete the
whole `addons/multiaddonmanager` folder too if you want it fully gone.

## Once verified: unlocks (separate future work, not started)
Custom kick/bounce sounds (highest feel value, smallest scope — the 4
original SoMoE wavs are already local at
`ball-reference-analysis/somoe19-original/addons/sourcemod/sound/soccermod/`),
shouts, jerseys/GK skins, and eventually a patched stadium map addon (the
roof-scoreboard PVS issue and the CSF logo removal both need a map edit —
see the two "not fixable from the plugin" conclusions in the session
history — MultiAddonManager is how a second, patched-map addon would ride
alongside the original workshop map).
