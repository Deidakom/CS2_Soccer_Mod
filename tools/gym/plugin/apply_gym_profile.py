#!/usr/bin/env python3
"""SoccerMod gym (soccer_2v2_arena, owner 2026-10-02: a small 2v2 hall with a wooden floor).

Run tools/hall/plugin/apply_hall_atmo.py first: it rewrites HallLayout.cs, where the gym now counts
as a hall map (the map's own fans cheer, no stadium sounds, no celebrations). Here: the map profile
(one pitch centred on the origin, floor at the v8 height, the 2v2 goal of soccer_multi_indoor -
mouth 156 x 62 - set into the end walls, goal line = the wall's face at y +-720; the map brings its
own goal frame and net, so no v8 goal extras), and no pitch designs on the parquet.
Numbers from tools/gym/layout.mjs. Exact-match edits, idempotent, with backups.

  python3 apply_gym_profile.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "MapProfiles.cs": [
        (
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall };\n",
            "    // 2026-10-02 owner: a small hall for 2v2 (tools/gym): parquet floor, a cage on the long sides, the end\n"
            "    // walls in play. Centre spot = origin, floor = the v8 floor. The goals are soccer_multi_indoor's 2v2\n"
            "    // goal (mouth +-78 x 62) set into the end walls: the goal line is the wall's face. The map has its own\n"
            "    // goal frame and net. Bounds = the court with the two goal recesses behind the lines.\n"
            "    internal static readonly MapProfile Gym = new(\n"
            '        "soccer_2v2_arena",\n'
            '        new[] { "filter_ball" },\n'
            '        "prop_physics_multiplayer",\n'
            "        18.805f,\n"
            '        "gym",\n'
            "        new PitchFrame[]\n"
            "        {\n"
            '            new("gym", "Gym 2v2", 0.0f, 0.0f, -32.0f, 720.0f, 78.0f, 62.0f, true, -530, 530, -800, 800, ""),\n'
            "        },\n"
            "        System.Array.Empty<string>());\n"
            "\n"
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall, Gym };\n",
        ),
    ],
    "SoccerModMvpPlugin.PitchDesign.cs": [
        (
            "    private bool PitchDesignAvailable => _pitchDesignPrecached && FlagFileOn(PitchDesignFlagFile) && (IsFoundationMap(_currentMapName) || OnHall);",
            "    private bool PitchDesignAvailable => _pitchDesignPrecached && FlagFileOn(PitchDesignFlagFile) && (IsFoundationMap(_currentMapName) || OnHall && !OnGym);   // the gym has a parquet floor",
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
