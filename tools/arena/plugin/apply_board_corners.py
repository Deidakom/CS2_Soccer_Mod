#!/usr/bin/env python3
"""Owner 2026-10-01: "the corner seems a bit buggy for the wall" (stadium, LED boards).

With the 2-unit inset the side and end boards crossed each other at the corners, and the side
boards (scale 0.96) were 2 units lower than the end boards (scale 1.0), so the frames did not
meet. On the stadium the side boards now stop at the end boards' front face, the end boards run
across the side boards' depth, and the end boards sit a little lower so both tops are level.
v8 (inset 0) keeps its numbers. Exact-match edits in AtmoBoards.cs, idempotent, with a backup.

  python3 apply_board_corners.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

path = Path(sys.argv[1]) / "SoccerModMvpPlugin.AtmoBoards.cs"
raw = path.read_bytes().decode("utf-8")
eol = "\r\n" if "\r\n" in raw else "\n"

# (old, new, expected count)
EDITS = [
    (
        "        float Depth() => -inset;\n",
        "        float Depth() => -inset;\n"
        "        // Stadium (inset > 0): clean corners - the side boards end at the end boards' front face, the end\n"
        "        // boards run across the side boards' depth (6 u), and the end boards sit lower by the height\n"
        "        // difference of the two scales, so the top frames are level (owner 2026-10-01).\n"
        "        var sideTo = inset > 0f ? AtmoBoardEndFaceY - inset : 1665f;\n"
        "        var endHalf = inset > 0f ? AtmoBoardSideFaceX - inset + 6f : 1280f;\n"
        "        var sideScale = inset > 0f ? (sideTo - 129f) / 5f / AtmoBoardWidth : AtmoBoardSideScale;\n"
        "        var endScale = inset > 0f ? 2f * endHalf / 8f / AtmoBoardWidth : AtmoBoardEndScale;\n"
        "        var endZ = StadiumPitchPlaneZ - (inset > 0f ? 50f * (endScale - sideScale) : 0f);\n",
        1,
    ),
    ("Centres(-1665f, -129f, 5).Concat(Centres(129f, 1665f, 5))", "Centres(-sideTo, -129f, 5).Concat(Centres(129f, sideTo, 5))", 2),
    ("Centres(-1280f, 1280f, 8)", "Centres(-endHalf, endHalf, 8)", 2),
    ("StadiumPitchPlaneZ), 180f, AtmoBoardSideScale);", "StadiumPitchPlaneZ), 180f, sideScale);", 1),
    ("StadiumPitchPlaneZ), 0f, AtmoBoardSideScale);", "StadiumPitchPlaneZ), 0f, sideScale);", 1),
    ("StadiumPitchPlaneZ), 270f, AtmoBoardEndScale);", "endZ), 270f, endScale);", 1),
    ("StadiumPitchPlaneZ), 90f, AtmoBoardEndScale);", "endZ), 90f, endScale);", 1),
]

new = raw
if "var sideTo = inset > 0f" in new:
    print("already applied")
    sys.exit(0)
for old, repl, count in EDITS:
    old, repl = old.replace("\n", eol), repl.replace("\n", eol)
    if new.count(old) != count:
        sys.exit(f"expected {count} match(es), found {new.count(old)} for:\n{old}")
    new = new.replace(old, repl)
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))
backup.mkdir(parents=True, exist_ok=True)
shutil.copy2(path, backup / path.name)
path.write_bytes(new.encode("utf-8"))
print("edited", path.name, "- backup in", backup)
