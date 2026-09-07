# Goalkeeper trial deployment — 2026-09-07

Installed on `cs2-soccermod-test.service` at 08:41 UTC using the targeted
installer in `preserve` mode. Server and SoccerMod started successfully on
`soccer_cssl_stadium_v8`, `mp_maxrounds=999999`, with no exceptions in the checked
startup log. `css_gktest` reported OFF after startup; `css_gktest on` then reported
ON with zero participants. Individual opt-in remains required.

DLL SHA256: `2ae859edf61f28c50c410275bfce03a368c2f4a989fda9bd31279f0e17c23db6`

Backup: `/home/gameserver/cs2-soccermod-backups/ball-handling-20260907T084147Z-yW6dG2`

Rollback: `bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T084147Z-yW6dG2/rollback.sh`

The immediately preceding build contains the airborne-direction fix. The trial
build retains that correction. To disable only the experiment without restarting,
use `css_gktest off`. See `docs/goalkeeper-trial.md` for controls, limitations,
validation and user playtest steps. Client gameplay has not been tested by Codex.
