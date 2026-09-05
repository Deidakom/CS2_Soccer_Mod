# Lower stamina HUD — 1.4.8-dev

Deployed on the German CS2 server at 10:45 UTC on 2026-09-05.

The compact meter now occupies the last row of the screen-space panel, toward
the space between the hands. Two small rows precede it; live match information
uses those rows, so switching between warmup and a match does not change the
meter's row. The blue active/red refilling colours and central white percentage
are retained. The native HTML panel still owns its absolute screen coordinates;
exact placement between hands needs player observation. A dedicated Panorama
panel remains the route to independent pixel/percentage positioning.

Validation: 105 Node tests and the managed suite passed, including score-above-
meter ordering, matching row counts, and active/refill colour checks.

DLL SHA-256: `23f061bb8b97760f8722b864c191b05abab2193686eee64b51b51ac42c453c73`.

Rollback to 1.4.7-dev:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T104503Z-e5wYc4/rollback.sh
```

This is an immediate stamina-only update. The full overview menu and fixed
navigation redesign for the dedicated Workshop panel are still pending.
