# Airborne incoming early touch — deployed 2026-09-07 12:45 UTC

Early-touch reach penalty and cushioning now additionally require an airborne
ball. The shared grounded test is evaluated before both adjustments. Incoming
ground balls retain their normal kick result at maximum reach. Existing
aim-based soft-pass/pitch controls remain unchanged.

All managed regression groups and 18 focused Node checks passed. Installed DLL:
`da4bcac27a67f888a348a7747ec3df603abef2bb40b34fa3671c269f3eb05b61`.

Rollback:
`bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T124501Z-cBIV1a/rollback.sh`

Brief restart in preserve mode; no map or client changes. Previous live wall
values (conversion 2, maxAdded 0, normal retention 0.225) were preserved.
RCON revision identifies `earlyTouch=airborne-and-incoming-only`. In-game feel
remains user-tested; no client was controlled.
