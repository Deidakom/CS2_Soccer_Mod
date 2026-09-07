# Lighter distance power penalty — deployed 2026-09-07 10:41 UTC

User found the previous reduction too strong at normal knife distances.
Full added kick impulse now extends to 70% of surface reach (was 35%).
The outer 30% smoothly tapers to 75% impulse (was 35%). At 85% reach,
power is 87.5%. Standing/crouch, ground/air and inherited momentum remain.

Wall-pop eligibility is explicitly kept at its prior close-contact boundary
(35% surface reach), so expanding normal full power does not expand wall pops.

Managed regressions and three focused balanced-ball checks pass. DLL SHA256:
`df5d2904ec91829ababb22b4f7fa34ab008ec8adedeaed3a999fa3eb0ca73b46`

Backup/rollback:
`bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260907T104121Z-DFRwRp/rollback.sh`

Installed in preserve mode. GK trial was ON with one participant before restart;
server-wide ON restored, personal opt-in resets by design. No in-game strength
judgment claimed; this is the requested tuning revision.
