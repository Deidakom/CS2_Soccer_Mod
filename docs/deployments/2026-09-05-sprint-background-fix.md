# Sprint background correction — 1.4.9-dev

Deployed to the German CS2 server at 10:54 UTC on 2026-09-05.

The player confirmed that 1.4.8 moved the meter lower but enlarged its background.
The cause was the two empty rows used to offset it. Those rows are removed;
without match score the HUD is again a single compact row. Real match score,
when enabled, still appears above the meter. Colours and percentage layout are
unchanged.

This fixes the inflated background, not independent lower positioning. Native
PrintToCenterHtml owns the panel's coordinates. A dedicated Panorama panel on
Windows is still required to keep the compact box at the requested lower screen
position. Do not restore padding as a substitute for positioning.

All 105 Node tests and the managed suite passed, including absence of blank rows
and nonbreaking-space padding, score ordering and active/refill colours.

DLL SHA-256: `f4349d171cbfa5ef2d78dca538a7547b684e911a7c5692c69591226f0da00257`.

Rollback to 1.4.8-dev:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T105455Z-XTtxzb/rollback.sh
```

The Windows session should pull this update before further UI changes. Current
menu limitations remain as documented in the Windows handover.
