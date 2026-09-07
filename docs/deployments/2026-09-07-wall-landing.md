# Wall/landing lift correction — 2026-09-07

User reported occasional excessive ground bounce after wall play. Recent logs
confirmed wall assistance adding up to 200 units/s of vertical speed, but did
not identify the exact screenshot event or prove its exclusive cause.

Corrected two code paths capable of exaggerating that behaviour:

- Both profiles suppress extra wall lift near the floor when their recent
  samples include downward speed greater than 80 units/s. Existing upward
  speed counts toward the wall-lift target instead of stacking a full bonus.
- Legacy separation now adjusts only the current wall-normal component, using
  a fresh movement sample and confirming contact with that wall. It no longer
  replays the original XYZ velocity for four frames across gravity/landings.
  It stops on new contacts, profile/setting changes, resets, freezes or loss
  of wall contact. Native floor restitution and configured lift dials remain.

The legacy contact generation is now invalidated on new kicks, body pushes,
body impacts and catches too. Additional `wall_lift_limited` diagnostics record
suppression decisions. Exact client reproduction remains for the user.

Validation: 22 new wall/landing checks and all managed regressions pass,
including volleys and goalkeeper trial. Existing unrelated Node CRLF-sensitive
spectator bind assertion was already documented in earlier deployments.

Deployed SHA256: `9eabfccaf2a626e258fb805aa101796d4781b3fca18a678b31c0bf099ce1a07e`

Backup: `/home/gameserver/cs2-soccermod-backups/ball-handling-20260907T084703Z-BOaubo`

Server restarted and loaded the stadium/SoсcerMod. GK trial was ON beforehand
and restored ON after restart; per-player opt-in is intentionally session-only.
