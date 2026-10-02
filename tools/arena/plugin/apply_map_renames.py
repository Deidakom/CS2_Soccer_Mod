#!/usr/bin/env python3
"""Owner 2026-10-02: new map names -
  ka_soccermod_stadium -> soccer_soccermod_arena ("the name was better"), ka_soccermod_indoor -> soccer_indoor_hall,
  the new 2v2 hall = soccer_2v2_arena.

Run after tools/hall/plugin/apply_hall_atmo.py (HallLayout.cs: both hall names, the 2v2 map) and
tools/gym/plugin/apply_gym_profile.py (profile "Gym"). Here: the stadium's two names swap places
(both still count, so a server on an older Workshop revision keeps working), the hall's profile
answers to both names, and the default map list shows the new names.
Exact-match edits, idempotent, with backups.

  python3 apply_map_renames.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "SoccerModMvpPlugin.ArenaLayout.cs": [
        (
            "// ka_soccermod_stadium, first called soccer_soccermod_arena (2026-10-01,",
            "// soccer_soccermod_arena, for two Workshop updates called ka_soccermod_stadium (2026-10-01,",
        ),
        (
            "    // 2026-10-01 owner: the map is called ka_soccermod_stadium. Its first Workshop revisions were\n"
            "    // soccer_soccermod_arena; both names count, so a server still on an old revision keeps working.\n"
            '    private const string ArenaMapName = "ka_soccermod_stadium";\n'
            '    private const string ArenaFirstMapName = "soccer_soccermod_arena";\n',
            "    // 2026-10-02 owner: the map is called soccer_soccermod_arena again (\"the name was better\"). Workshop\n"
            "    // updates 3 and 4 carried it as ka_soccermod_stadium; both names count, so a server still on an old\n"
            "    // revision keeps working.\n"
            '    private const string ArenaMapName = "soccer_soccermod_arena";\n'
            '    private const string ArenaFirstMapName = "ka_soccermod_stadium";\n',
        ),
    ],
    "SoccerModMvpPlugin.Match.cs": [
        (
            "    // 2026-10-01 owner: our own stadium ka_soccermod_stadium is the default map.",
            "    // 2026-10-01 owner: our own stadium soccer_soccermod_arena is the default map.",
        ),
    ],
    "MapProfiles.cs": [
        (
            '        "ka_soccermod_indoor",\n        new[] { "filter_ball" },\n        "prop_physics_multiplayer",\n        18.805f,\n        "hall",\n',
            '        "soccer_indoor_hall",\n        new[] { "filter_ball" },\n        "prop_physics_multiplayer",\n        18.805f,\n        "hall",\n',
        ),
        (
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall, Gym };\n",
            "    // 2026-10-02 owner: the hall is called soccer_indoor_hall; its first Workshop revisions were ka_soccermod_indoor.\n"
            '    internal static readonly MapProfile HallFirst = Hall with { MapName = "ka_soccermod_indoor" };\n'
            "\n"
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall, HallFirst, Gym };\n",
        ),
    ],
    "SoccerModMvpPlugin.MapSelect.cs": [
        (
            'new() { Name = "SoccerMod Stadium (ka_soccermod_stadium)", Workshop = StadiumWorkshopId },',
            'new() { Name = "SoccerMod Arena (soccer_soccermod_arena)", Workshop = StadiumWorkshopId },',
        ),
        (
            'new() { Name = "SoccerMod Indoor Hall (ka_soccermod_indoor)", Workshop = "3811545272" },',
            'new() { Name = "SoccerMod Indoor Hall (soccer_indoor_hall)", Workshop = "3811545272" },',
        ),
        (   # owner 2026-10-02: the 2v2 map is official - it is in the default list too
            '            new() { Name = "SoccerMod Indoor Hall (soccer_indoor_hall)", Workshop = "3811545272" },\n',
            '            new() { Name = "SoccerMod Indoor Hall (soccer_indoor_hall)", Workshop = "3811545272" },\n'
            '            new() { Name = "SoccerMod 2v2 Arena (soccer_2v2_arena)", Workshop = "3811585232" },\n',
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
