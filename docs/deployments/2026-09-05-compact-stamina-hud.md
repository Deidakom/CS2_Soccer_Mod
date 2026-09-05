# Compact stamina HUD — 1.4.6-dev

Deployed on the German CS2 server at 10:30 UTC, 2026-09-05.

The percentage now sits between two ten-segment wings instead of to the right
of the meter. Twenty total segments widen the meter and give 5% increments.
Text changes from fontSize-l to fontSize-m, and empty score rows are removed;
the no-score display is a single centred row. This targets a roughly 30% smaller
height with a wider shape. The CS2 HTML panel owns its outer dimensions, so an
exact pixel reduction is not guaranteed and requires player observation.

The bar is cyan-blue while sprinting and red while inactive with stamina below
full (refilling/cooldown). The percentage stays white. A full, always-visible bar
returns to blue. Existing speed, stamina calculation and visibility preferences
are unchanged. A live match still includes its escaped score text below.

Validation: all 105 Node tests and the managed suite passed. Regression checks
cover 0/55/100%, bounds, active blue, refill red, full blue and removal of blank
rows. Server startup and visual client checks are separate; no exact size or
pixel-alignment measurement is claimed.

DLL SHA-256: `6f6caff20081c37bbc4bbc52aa34619b15bfc999656d1681c619d2fa03b60be8`.

Rollback to 1.4.5-dev on the server:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T103048Z-tayOU7/rollback.sh
```
