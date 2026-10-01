#!/usr/bin/env python3
"""Owner feedback 2026-10-01 (first arena test), applied in place in the plugin tree.

  1. Own goal: the team that gets the goal celebrates (team show, crowd roar); no personal celebration.
  2. Arena: the LED boards are the only pitch border look. The map's own low boards stay as the
     wall (collision); the LED boards stand 2 units in front of them, so the two no longer flicker.

Exact-match edits, idempotent, every touched file is backed up first.
  python3 apply_arena_round2.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "SoccerModMvpPlugin.Atmosphere.cs": [
        (
            "        if (kind != AtmoMoment.OwnGoal) AtmoGoalShow(scoringTeam, planeY > 0 ? 1 : -1, _lastKickerSlot);\n",
            "        // 2026-10-01 owner: an own goal is celebrated too - by the team that gets it (no personal celebration).\n"
            "        AtmoGoalShow(scoringTeam, planeY > 0 ? 1 : -1, kind == AtmoMoment.OwnGoal ? -1 : _lastKickerSlot);\n",
        ),
    ],
    "SoccerModMvpPlugin.AtmoCrowdSound.cs": [
        (
            "            case AtmoMoment.OwnGoal:\n"
            "                AtmoLater(0.4, () => AtmoCrowdSound(\"Whistle\"));\n",
            "            case AtmoMoment.OwnGoal:\n"
            "                // 2026-10-01 owner: the other team's fans cheer, then the unlucky side whistles.\n"
            "                AtmoLater(0.2, () => AtmoCrowdSound(\"Roar\"));\n"
            "                AtmoLater(1.6, () => AtmoCrowdSound(\"Whistle\"));\n",
        ),
    ],
    "SoccerModMvpPlugin.AtmoBoards.cs": [
        (
            "    private static IEnumerable<(Vector At, float Yaw, float Scale)> AtmoBoardSpots()\n",
            "    // inset: how far the boards stand in front of the wall line. The arena has its own low boards\n"
            "    // there (world geometry, the wall); on the same plane the two flickered (owner 2026-10-01).\n"
            "    private const float ArenaBoardInset = 2f;\n"
            "    private static IEnumerable<(Vector At, float Yaw, float Scale)> AtmoBoardSpots(float inset = 0f)\n",
        ),
        (
            "        static float Depth() => 0f;\n",
            "        float Depth() => -inset;\n",
        ),
        (
            "        foreach (var (at, yaw, scale) in AtmoBoardSpots())\n",
            "        foreach (var (at, yaw, scale) in AtmoBoardSpots(OnArena ? ArenaBoardInset : 0f))\n",
        ),
    ],
    "SoccerModMvpPlugin.PerimeterWall.cs": [
        (
            "            var shown = PitchBorderShown(receiver);\n",
            "            // 2026-10-01 owner: on the arena the LED boards are the only border look (the map's own\n"
            "            // boards behind them are the wall and show when the LED boards are switched off).\n"
            "            var shown = OnArena ? 2 : PitchBorderShown(receiver);\n",
        ),
    ],
    "SoccerModMvpPlugin.Menu.cs": [
        (
            "        if (PerimeterWallAvailable)\n"
            "        {\n"
            "            // 2026-09-29: three borders (LED boards first choice, PerimeterWall.cs).\n",
            "        if (PerimeterWallAvailable && !OnArena)   // arena: LED boards only (owner 2026-10-01)\n"
            "        {\n"
            "            // 2026-09-29: three borders (LED boards first choice, PerimeterWall.cs).\n",
        ),
    ],
}

changed = 0
for name, edits in EDITS.items():
    path = src / name
    text = path.read_text(encoding="utf-8")
    new = text
    for old, repl in edits:
        if repl in new:
            continue
        if new.count(old) != 1:
            sys.exit(f"{name}: expected exactly one match for:\n{old}")
        new = new.replace(old, repl)
    if new != text:
        backup.mkdir(parents=True, exist_ok=True)
        shutil.copy2(path, backup / name)
        path.write_text(new, encoding="utf-8")
        changed += 1
        print("edited", name)
    else:
        print("already done", name)
print(f"{changed} file(s) changed" + (f", backups in {backup}" if changed else ""))
