#!/usr/bin/env python3
"""Owner 2026-10-02: "also disable the stadium celebration stuff from the map" - "fans can celebrate
but no other effect" (ka_soccermod_indoor).

Run apply_hall_atmo.py first: it rewrites HallLayout.cs, where HallFx now lets only the ball's own
effects through. Here: no personal goal celebration and no matchday show in the hall, and the
"Goal celebration" entry is not in the settings menu on that map. The fans' goal clips stay.
Exact-match edits, idempotent, with backups.

  python3 apply_hall_no_celebrations.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "SoccerModMvpPlugin.AtmoGoalFx.cs": [
        (
            "        if (!AtmoSet.GoalCelebrations || scorerSlot < 0 || ",
            "        if (OnHall || !AtmoSet.GoalCelebrations || scorerSlot < 0 || ",   # no celebrations in the indoor hall
        ),
        (
            "        if (!AtmoOn || !AtmoSet.GoalCelebrations) return;\n        menu.Add(\"Goal celebration\", OpenGoalFxMenu);",
            "        if (!AtmoOn || OnHall || !AtmoSet.GoalCelebrations) return;   // not offered in the indoor hall\n        menu.Add(\"Goal celebration\", OpenGoalFxMenu);",
        ),
    ],
    "SoccerModMvpPlugin.AtmoShow.cs": [
        (
            "        if (!AtmoOn || !AtmoSet.MatchdayShow) return;",
            "        if (!AtmoOn || OnHall || !AtmoSet.MatchdayShow) return;   // no show in the indoor hall",
        ),
    ],
}

plan = {}
for name, edits in EDITS.items():
    path = src / name
    raw = path.read_bytes().decode("utf-8")
    eol = "\r\n" if "\r\n" in raw else "\n"
    new = raw
    for old, repl in edits:
        old, repl = old.replace("\n", eol), repl.replace("\n", eol)
        if repl in new:
            continue
        if new.count(old) != 1:
            sys.exit(f"{name}: expected exactly one match for:\n{old}")
        new = new.replace(old, repl)
    if new != raw:
        plan[path] = new
for path, new in plan.items():
    backup.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, backup / path.name)
    path.write_bytes(new.encode("utf-8"))
    print("edited", path.name)
print(f"{len(plan)} file(s) changed" + (f", backups in {backup}" if plan else " (already applied)"))
