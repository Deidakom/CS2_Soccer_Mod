# Menu layout redesign — 1.4.5-dev

Deployed to the German CS2 server at 10:25 UTC on 2026-09-05. The live server
already selected HTML mode, which remains selected and persisted.

The centre-screen HTML renderer now has a large white heading on every page,
a cyan page indicator, five action rows in medium-size white text with cyan
number keys, and a separate compact navigation footer. Back/Previous is always
8, Next is 9 and Close is 0. Navigation no longer consumes action capacity;
most nested pages previously had only four choices. Informational rows remain
nonselectable and use a softer blue-grey colour. Nested headings show the final
section name instead of the full repeated path. The inactivity timeout is now
60 seconds rather than 30 seconds.

Dynamic headings and labels are HTML-escaped. The old proportional-space
padding has been removed; it increased clutter without providing real alignment.
All menu actions, feature branches, permission checks and spectator input routes
remain available. Plain mode retains its measured compact fallback; the classic
addon renderer retains seven choices per page.

Validation: 105 Node tests and the managed suite passed. New managed tests
exercise the real renderer for 0, 1, 5, 6, 10, 11 and 46 options, checking that
no option is lost, duplicated or reordered, navigation cannot collide with
choices, all pages retain headings/page counts and dynamic markup is escaped.
Large-font clipping and visual steadiness still require connected-player checks.

This redesign does not activate a resizable Panorama side panel. The companion
Workshop UI is still not published/mounted alongside the third-party stadium;
loose server resources cannot satisfy that client distribution/trust requirement.
See `docs/2026-08-31-classic-menu-implementation.md` for the activation boundary.

DLL SHA-256: `18ab954d32d69dac415101b4d80bb35790cf04eeb9f0a908bf99989ee6d6c298`.

Rollback to 1.4.4-dev:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T102545Z-ecy4JN/rollback.sh
```
