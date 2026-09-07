# Incoming-only early touch — deployed 2026-09-07 12:40 UTC

Both the outer-reach power penalty and cushioning now require the ball to move
toward the player's body and close relative to player movement. Stationary,
outgoing, sideways and chased balls retain their normal kick result. A 5 u/s
closing threshold ignores resting jitter; overhead downward approach qualifies.
Full-strength inner 90% reach and existing incoming tip strength are unchanged.
Diagnostic earlyBlend now reports zero when cushioning is bypassed.

All managed regression groups and 18 focused Node checks passed. Installed DLL:
`277b90ec8e147cfd23e47e8976f0837b67430adc6f85e3f5f4f43ad069ea674f`.

Backup/rollback:
`bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T124004Z-FyUDyk/rollback.sh`

Deployed in preserve mode to `212.87.212.58:27017`; brief restart, no client
control. Current player-adjusted wall retention was 0.225 before deployment
and was preserved, not reset to the source default. Gameplay confirmation
remains with the user.
