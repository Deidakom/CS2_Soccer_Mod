# Menu clipping and navigation correction — 1.4.7-dev

Deployed to the German CS2 server at 10:35 UTC on 2026-09-05.

Player feedback established that the larger five-row HTML layout overflowed
CS2's fixed centre-panel height, hiding its navigation footer. Input logs at
10:27 UTC show repeated key 6 attempts on the root menu while the redesign had
moved Next to 9. The same logs confirm key 0 successfully closed menus.

The corrected layout uses three medium-size choices, a medium heading, and a
navigation row immediately beneath the heading: five explicit rows maximum.
Navigation uses consecutive keys after the choices again. Keys 8/9 also act as
Back/Previous and Next shortcuts; 0 closes. The default zero-key slot10 command
is now listened for alongside the existing css_0 and slot0 routes. No client
bindings are overwritten or claimed to be forced by the server.

Every feature remains paginated and reachable. The tradeoff is fewer simultaneous
choices within the native panel's fixed height. Full-screen/resizable layout
still requires distributing and mounting the Workshop UI addon. Long labels may
wrap according to the client's font metrics; visual confirmation remains needed.

The compact stamina bar and active/refill colours from 1.4.6-dev are retained.

Validation: 105 Node tests and the managed suite passed. Menu cases cover empty,
boundary and 46-option lists, complete ordered reachability, consecutive keys,
HTML escaping, navigation preceding choices and a maximum five explicit rows.

DLL SHA-256: `ae78d112d2eee9e66468bd1f19c7e24748b91c2411d9ada59fcbde237b4b245a`.

Rollback to 1.4.6-dev (including its older five-choice menu layout):

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T103552Z-2eTAAe/rollback.sh
```
