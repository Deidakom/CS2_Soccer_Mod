#!/usr/bin/env python3
"""Owner 2026-10-01: ka_soccermod_stadium (Workshop 3811382872) is the default map and the one
Workshop item. Same edits as commit 6752eb9 on main, applied in place (exact match, idempotent).

  python3 apply_default_stadium.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "SoccerModMvpPlugin.Match.cs": [
        (
            '    private const string LegacyStadiumWorkshopId = "3361075564";\n',
            '    private const string LegacyStadiumWorkshopId = "3361075564";\n'
            "    // 2026-10-01 owner: our own stadium ka_soccermod_stadium is the default map. Its Workshop item also\n"
            "    // carries everything the plugin shows and plays, so servers and players need this one item only.\n"
            '    private const string StadiumWorkshopId = "3811382872";\n',
        ),
    ],
    "SoccerModMvpPlugin.AutoMap.cs": [
        ("non-soccer map to the SoccerMod stadium (Workshop 3361075564). Only once:", "non-soccer map to the SoccerMod stadium (Workshop 3811382872). Only once:"),
        ('to=workshop:{Id}", map, LegacyStadiumWorkshopId);', 'to=workshop:{Id}", map, StadiumWorkshopId);'),
        ('host_workshop_map {LegacyStadiumWorkshopId}");', 'host_workshop_map {StadiumWorkshopId}");'),
    ],
    "SoccerModMvpPlugin.MapSelect.cs": [
        ('            new() { Name = "Soccer Stadium (CSSL v8)", Workshop = LegacyStadiumWorkshopId },', '            new() { Name = "SoccerMod Stadium (ka_soccermod_stadium)", Workshop = StadiumWorkshopId },'),
        ('            new() { Name = "SoccerMod Stadium (test)", Workshop = "3807839299" },', '            new() { Name = "Soccer_Multi_Indoor", Workshop = "3809626481" },'),
    ],
    "SoccerModMvpPlugin.Links.cs": [
        (
            '        ("SoccerMod Feature Package (Workshop)", "https://steamcommunity.com/sharedfiles/filedetails/?id=3797479770"),',
            "        // 2026-10-01: one item - the stadium map plus everything the plugin shows and plays (was the Feature Package 3797479770).\n"
            '        ("SoccerMod stadium + features (Workshop)", "https://steamcommunity.com/sharedfiles/filedetails/?id=3811382872"),',
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
