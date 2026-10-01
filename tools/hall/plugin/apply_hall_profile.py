#!/usr/bin/env python3
"""SoccerMod indoor hall (ka_soccermod_indoor): the map profile for the plugin.

One pitch centred on the origin, floor at the v8 height, v8 goal mouth set into the end boards
(goal line = the boards' inner face, y +-1150). Numbers from tools/hall/layout.mjs. Also: a
one-pitch profile has no switch button and no "Pitch size" menu entry.
Exact-match edits, idempotent, with backups.

  python3 apply_hall_profile.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "MapProfiles.cs": [
        (
            "    internal static readonly MapProfile[] All = { MultiIndoor };\n",
            "    // 2026-10-01 owner: our own indoor hall (tools/hall): a boarded court for 3 to 4 players per team.\n"
            "    // Centre spot = origin, floor = the v8 floor, so pitch-local numbers are world numbers. The goals\n"
            "    // are v8's mouth (posts at +-128, crossbar at +101) set into the end boards: the goal line is the\n"
            "    // boards' inner face. Bounds = the court with the two goal housings behind the lines.\n"
            "    internal static readonly MapProfile Hall = new(\n"
            '        "ka_soccermod_indoor",\n'
            '        new[] { "filter_ball" },\n'
            '        "prop_physics_multiplayer",\n'
            "        18.805f,\n"
            '        "hall",\n'
            "        new PitchFrame[]\n"
            "        {\n"
            '            new("hall", "Indoor hall", 0.0f, 0.0f, -32.0f, 1150.0f, 124.0f, 97.0f, true, -850, 850, -1380, 1380, "", V8Goals: true),\n'
            "        },\n"
            "        System.Array.Empty<string>());\n"
            "\n"
            "    internal static readonly MapProfile[] All = { MultiIndoor, Hall };\n",
        ),
    ],
    "SoccerModMvpPlugin.MapProfile.cs": [
        (
            "        var pressed = false;\n        foreach (var button in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(\"func_button\"))\n",
            "        var pressed = frame.Button.Length == 0;   // a one-pitch map (the indoor hall) has no switch\n        foreach (var button in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(\"func_button\"))\n",
        ),
    ],
    "SoccerModMvpPlugin.Menu.cs": [
        (
            '        if (ActiveProfile is not null) menu.Add($"Pitch size: {ActiveFrame?.Label}" + AccessTag(player, "SM"), OpenPitchSizeMenu);',
            '        if (ActiveProfile is { Frames.Length: > 1 }) menu.Add($"Pitch size: {ActiveFrame?.Label}" + AccessTag(player, "SM"), OpenPitchSizeMenu);',
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
