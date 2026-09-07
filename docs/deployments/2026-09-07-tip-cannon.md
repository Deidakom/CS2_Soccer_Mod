# Tip contact, frequent rebounds and cannon goal suppression

Deployed 2026-09-07 12:30 UTC to `212.87.212.58:27017`.

- Full-power reach expanded from 82% to 90%. Only the final 10% blends into
  cushioning. Maximum tip pass reduced from 200 to 100 u/s; incoming shots
  can still be stopped. Closer hits retain normal power.
- Wall assistance cooldown reduced from 0.35 to 0.18 s. Live retention remains
  0.175, with no added wall lift. Existing contact checks/separation guards remain.
- Any active shared/personal cannon suppresses goal detection before its first
  shot. The last cannon stopping releases automatic suppression. Manual goal
  disabling is untouched. The training menu explains and enforces the lock.

All managed regression groups passed, including the actual goal-entry gate in
warmup/live phases and multi-cannon/manual-setting cases. All 17 focused Node
checks passed. NU1900 vulnerability-feed warning remains environmental.

Installed DLL SHA256:
`1d86eb94e3e2d989d5ba798739cc5394e7e1f85b90ae105343cce9861bb4436c`

Backup/rollback:
`bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T123005Z-9bJsth/rollback.sh`

Used the existing inspected installer in preserve mode. Brief restart; map,
hostname, other physics settings and player data preserved. RCON confirmed
`feel=2026-09-07-tip-cannon`, fullReach=90%, tipPass<=100, wallRetention=0.175,
cooldown=0.18s, and cannonGoalsSuppressed=False with no cannons running after
restart. Service active and no plugin exceptions in the inspected startup log.
No client control or live cannon launch was performed; user gameplay verification
is still needed. No Git commit/push was made in this task.
