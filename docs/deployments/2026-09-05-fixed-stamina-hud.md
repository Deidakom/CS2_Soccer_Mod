# Fixed stamina HUD — 1.4.4-dev

Deployed to the German CS2 server at 10:16 UTC on 2026-09-05.

The stamina bar now renders through the client's screen-space HTML HUD rather
than a point_worldtext entity teleported to server-sampled view angles. There
is no camera-following transform or world-entity transmission filter anymore.
The bar uses larger fontSize-l text, bright cyan (#66EEFF) segments and a white
percentage. The existing stamina and sprint preferences remain unchanged.

Menus clear and take priority over the bar. While the bar is visible, the match
score shares the same HTML panel rather than competing with it. Team names are
HTML-escaped, and blank score rows reserve the same space during warmup. Death,
spectating, disconnect, disabled preferences and CAP suppression clear the bar.

The existing guarded HTML fade suppression also covers the stamina display;
it still leaves genuine round restarts under engine control. Its known warmup
limitation remains: native HTML can pulse. The fixed position follows from
screen-space rendering, but brightness, size and perceived steadiness require
connected-player observation; no visual confirmation is claimed.

Validation: all 105 Node tests and the managed regression suite passed. HUD tests
cover bounded stamina text, visibility rules, escaped score text and reserved
row count. The wiring test excludes world text/teleports/view angles and checks
score/menu arbitration.

DLL SHA-256: `ee267d2dc8d8a7ecd0796fe5804098a6077bcab12896a63b1acab5596e049d04`.

Rollback to 1.4.3-dev:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T101657Z-3Fpu31/rollback.sh
```
