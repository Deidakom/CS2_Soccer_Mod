#!/usr/bin/env python3
"""SoccerMod 1v1 cage (soccer_1v1_cage, owner 2026-10-02: "a 1v1 map that is built in a cage ... a mix of
underground and indoor" - his pick: a drained swimming pool in an old bathhouse).

Run tools/hall/plugin/apply_hall_atmo.py first (it rewrites HallLayout.cs: the pool counts as a hall map -
its onlookers cheer, no stadium sounds, no celebrations, no pyro - and has no turf), then
tools/brands/plugin/apply_brand_boards.py (no advert boards there) and tools/street/plugin/apply_street.py
(this script's edits follow its lines). Here:
- the map profile "Pool": one pitch centred on the origin = the pool's bottom, floor at the v8 height, the
  2v2 goal of soccer_multi_indoor (mouth 156 x 62) set into the pool's end walls, goal line = the wall's
  face at y +-440; the map brings its own goal frame and net, so no v8 goal extras,
- no pitch designs on the tiles,
- the map in the default map list.
Numbers from tools/pool/layout.mjs. Exact-match edits, idempotent, with backups.

  python3 apply_pool.py /root/football-build/src/server-plugin/SoccerModMvp <workshop id | ->
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
workshop = sys.argv[2]
backup = Path("/root/arena-plugin") / ("backup-pool-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "MapProfiles.cs": [
        (
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall, HallFirst, Gym, Street };\n",
            "    // 2026-10-02 owner: a 1v1 map in a cage (tools/pool): the pitch is the bottom of a drained pool, its tiled\n"
            "    // walls are in play, a cage stands on the pool's edge. Centre spot = origin, floor = the v8 floor. The\n"
            "    // goals are soccer_multi_indoor's 2v2 goal (mouth +-78 x 62) set into the pool's end walls: the goal\n"
            "    // line is the wall's face. The map has its own goal frame and net.\n"
            "    internal static readonly MapProfile Pool = new(\n"
            '        "soccer_1v1_cage",\n'
            '        new[] { "filter_ball" },\n'
            '        "prop_physics_multiplayer",\n'
            "        18.805f,\n"
            '        "pool",\n'
            "        new PitchFrame[]\n"
            "        {\n"
            '            new("pool", "Pool 1v1", 0.0f, 0.0f, -32.0f, 440.0f, 78.0f, 62.0f, true, -310, 310, -520, 520, ""),\n'
            "        },\n"
            "        System.Array.Empty<string>());\n"
            "\n"
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall, HallFirst, Gym, Street, Pool };\n",
        ),
    ],
    "SoccerModMvpPlugin.PitchDesign.cs": [
        (
            "(IsFoundationMap(_currentMapName) || OnHall && !OnGym && !OnStreet);",
            "(IsFoundationMap(_currentMapName) || OnHall && !OnGym && !OnStreet && !OnPool);",
        ),
    ],
    "SoccerModMvpPlugin.MapSelect.cs": [
        (
            '            new() { Name = "SoccerMod Street Arena (soccer_street_arena)", Workshop = "',
            f'            new() {{ Name = "SoccerMod 1v1 Cage (soccer_1v1_cage)", Workshop = "{workshop}" }},\n'
            '            new() { Name = "SoccerMod Street Arena (soccer_street_arena)", Workshop = "',
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
