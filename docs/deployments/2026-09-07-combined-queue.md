# Combined queued fixes — deployed 2026-09-07 10:12 UTC

User authorized deploying all implemented changes. Release build and all
managed regression checks passed, as did the five focused GK-menu/low-ball
wiring checks. Includes low-ball surface-cone acceptance and fresh-kick
protection, direct server-side GK menu actions and rejection feedback, plus
the previously shipped volley-direction, wall-landing and goalkeeper trial work.

Deployed with `deploy/testserver/install-ball-handling.sh` in `preserve` mode
to `cs2-soccermod-test.service`, 212.87.212.58:27017. Server was empty.

- DLL SHA256: `9cd619dfdbd246436400ef1abc7a330d534cb8d8bda07f1f8417cb76176f7c38`
- Backup: `/home/gameserver/cs2-soccermod-backups/ball-handling-20260907T101201Z-2bape9`
- Rollback: `bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T101201Z-2bape9/rollback.sh`
- Verified active service, CS2 SoccerMod LOADED, stadium map loaded, legacy
  handling preserved, `mp_maxrounds=999999`.
- GK trial restored ON, matching pre-deployment status; zero opted-in players.

No client gameplay testing performed. Startup still reports engine/map/config
warnings; successful plugin loading is not a claim that all those warnings
are resolved. Broader physics redesign and distance-scaled kick strength are
still proposals and are NOT included in this deployment.

This supersedes the queued deployment status in the low-ball and GK-menu notes.
