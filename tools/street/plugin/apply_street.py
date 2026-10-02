#!/usr/bin/env python3
"""SoccerMod street arena (soccer_street_arena, owner 2026-10-02: a street court under an elevated
subway line - "FIFA Street style", graffiti instead of LED banners, onlookers in street clothes,
"the features of the other maps that are useful there", "no fireworks and stuff like that").

Run tools/hall/plugin/apply_hall_atmo.py first (it rewrites HallLayout.cs: the street arena counts
as a hall map - its onlookers cheer, no stadium sounds, no celebrations, no pyro - and has no turf)
and tools/brands/plugin/apply_brand_boards.py (BrandBoards.cs: no advert boards there). Here:
- the map profile "Street": one pitch centred on the origin, floor at the v8 height, v8's goal
  mouth (248 x 97) set into the end walls inside the two containers, goal line = the walls' face at
  y +-900; the plugin's goal frame, moving net and net pocket stand in the containers,
- no pitch designs on the asphalt,
- the map keeps its own sunset sky (the day sky of Sky.cs is not put over it),
- the map in the default map list.
Numbers from tools/street/layout.mjs. Exact-match edits, idempotent, with backups.

  python3 apply_street.py /root/football-build/src/server-plugin/SoccerModMvp <workshop id | ->
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
workshop = sys.argv[2]
backup = Path("/root/arena-plugin") / ("backup-street-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "MapProfiles.cs": [
        (
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall, HallFirst, Gym };\n",
            "    // 2026-10-02 owner: a street court for 3v3 under an elevated subway line (tools/street): asphalt, walls\n"
            "    // and fences in play. Centre spot = origin, floor = the v8 floor. The goals are v8's mouth set into the\n"
            "    // end walls, inside two cut-open shipping containers (the same housing as the indoor hall's goals).\n"
            "    internal static readonly MapProfile Street = new(\n"
            '        "soccer_street_arena",\n'
            '        new[] { "filter_ball" },\n'
            '        "prop_physics_multiplayer",\n'
            "        18.805f,\n"
            '        "street",\n'
            "        new PitchFrame[]\n"
            "        {\n"
            '            new("street", "Street arena", 0.0f, 0.0f, -32.0f, 900.0f, 124.0f, 97.0f, true, -660, 660, -1130, 1130, "", V8Goals: true),\n'
            "        },\n"
            "        System.Array.Empty<string>());\n"
            "\n"
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall, HallFirst, Gym, Street };\n",
        ),
    ],
    "SoccerModMvpPlugin.PitchDesign.cs": [
        (
            "(IsFoundationMap(_currentMapName) || OnHall && !OnGym);",
            "(IsFoundationMap(_currentMapName) || OnHall && !OnGym && !OnStreet);",
        ),
    ],
    "SoccerModMvpPlugin.Sky.cs": [
        (
            "    {\n        if (!_skyKloofWanted) return;\n        if (!_skyKloofPrecached)\n",
            "    {\n        if (!_skyKloofWanted) return;\n"
            "        if (IsStreetMap(Server.MapName)) return;   // the street arena keeps its own sunset sky (HallLayout.cs)\n"
            "        if (!_skyKloofPrecached)\n",
        ),
    ],
    "SoccerModMvpPlugin.MapSelect.cs": [
        (
            '            new() { Name = "SoccerMod 2v2 Arena (soccer_2v2_arena)", Workshop = "3811585232" },\n',
            '            new() { Name = "SoccerMod 2v2 Arena (soccer_2v2_arena)", Workshop = "3811585232" },\n'
            f'            new() {{ Name = "SoccerMod Street Arena (soccer_street_arena)", Workshop = "{workshop}" }},\n',
        ),
    ],
}

# "-" as the workshop id: the profile without the entry in the default map list (while the item is still unlisted)
if workshop == "-":
    del EDITS["SoccerModMvpPlugin.MapSelect.cs"]

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
