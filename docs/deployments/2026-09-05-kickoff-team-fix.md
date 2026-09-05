# Kickoff team and colour fix — 1.4.3-dev

Deployed to the German CS2 server on 2026-09-05 at 10:09 UTC.

Opening possession now uses a 50/50 draw between T and CT instead of hard-coded
CT. The selected engine side is retained for later regulation periods: because
players switch engine teams each period, the opposite squad receives the next
period's kickoff. Golden goal uses a fresh draw. Goals still award possession
to the conceding team, and round restarts do not redraw possession.

Home/T outlines are red; Away/CT outlines are DodgerBlue. The colour is applied
after beam spawn as well, and diagnostics report the possession team and colour.
The match-start announcement names the team awarded kickoff.

Validation: 105 Node tests and the managed regression suite passed, including
seeded draws selecting both valid teams, both colour mappings and existing
kickoff lifetime checks. Live server loaded all nine plugins; T and CT previews
reported 36 segments with Red and DodgerBlue respectively, then were cleared.
Client appearance still requires player observation.

DLL SHA-256: `e6b467a28cf95abf93c0ce2f4748d48d896618f2b681cf243fc0277eb9303a75`.

Rollback to the previous 1.4.2-dev build on the server:

```sh
bash /home/gameserver/cs2-soccermod-backups/ball-handling-20260905T100927Z-2puARc/rollback.sh
```

The installer preserved existing settings and created a fresh backup. CSS source
is unchanged by this CS2 correction.
