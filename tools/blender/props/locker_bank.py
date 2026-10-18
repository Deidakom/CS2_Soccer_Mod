# Four open player lockers on a wall (wall at +Y, open towards -Y) with a bench in front: name
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from lib import *
reset()
# plate, shirt with number on a hanger, boots below, towel on the bench. Home; variant away.
SHIRT = [(-0.5, 0), (0.5, 0), (0.5, 0.62), (0.84, 0.54), (0.98, 0.76), (0.56, 0.98), (0.2, 1.0), (0, 0.9),
         (-0.2, 1.0), (-0.56, 0.98), (-0.98, 0.76), (-0.84, 0.54), (-0.5, 0.62)]
N, CW, CD, CH = 4, 34, 22, 96
for k in range(N):
    x = -N * CW / 2 + CW * (k + 0.5)
    num = str([7, 10, 9, 4][k])
    box("locker_wood", (CW, 2, CH), (x, CD / 2, CH / 2 + 6))
    for s in (-1, 1):
        box("locker_wood", (1.6, CD, CH), (x + s * (CW / 2 - 0.8), 0, CH / 2 + 6))
    box("locker_wood", (CW, CD, 1.6), (x, 0, CH + 6))
    box("locker_wood", (CW, CD, 1.6), (x, 0, 22))
    box("locker_wood", (CW, CD, 1.6), (x, 0, CH - 10))
    box("team_paint", (CW - 6, 1, 6), (x, -CD / 2 - 0.2, CH + 1), bevel=0.3)
    text("jersey_number", num, 4, (x, -CD / 2 - 0.9, CH + 1), depth=0.2)
    sh = profile("jersey_home", [(px * 24, pz * 24) for px, pz in SHIRT], 1.4, bevel=0.3, segments=1)
    sh.location = (x, 2, 46)
    text("jersey_number", num, 9, (x, 0.8, 58), depth=0.2)
    tube("steel_brushed", [(x - 10, 2, 70), (x, 2, 74), (x + 10, 2, 70)], 0.5)
    for s in (-1, 1):
        sphere("boot", 1, (x + s * 5, -2, 26), scale=(4, 9, 3.2), segments=12, rings=6)
    box("towel", (14, 10, 1.4), (x + 6, -CD / 2 - 14, 20), bevel=0.6)
box("locker_wood", (N * CW, 14, 3), (0, -CD / 2 - 13, 18), bevel=0.8)
for x in (-N * CW / 2 + 8, N * CW / 2 - 8):
    box("steel_dark", (4, 10, 16), (x, -CD / 2 - 13, 8))
collision((N * CW, CD, CH + 8), (0, 0, (CH + 8) / 2))
collision((N * CW, 14, 20), (0, -CD / 2 - 13, 10))
PREVIEW_COLOURS.update(locker_wood=(0.55, 0.35, 0.18), team_paint=(0.7, 0.05, 0.06), jersey_home=(0.7, 0.05, 0.06), jersey_number=(1, 1, 1),
                       steel_brushed=(0.55, 0.56, 0.58), steel_dark=(0.1, 0.1, 0.1), boot=(0.05, 0.05, 0.05), towel=(0.95, 0.95, 0.95))
export(variants={"away": {"jersey_home": "jersey_away", "team_paint": "team_paint_away"}})
